// Deblowin - Windows debloat tool
// Copyright (C) 2026 Deblowin contributors
// Contains third-party material under GPL-3.0; see NOTICE.
// This program is free software: you can redistribute it and/or modify it under the terms
// of the GNU General Public License as published by the Free Software Foundation, version 3.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Deblowin
{
    // ---------------------------------------------------------------- catalog model

    public class LText
    {
        public string En;
        public string Pt;

        public string Get(string lang)
        {
            if (lang == "pt" && !string.IsNullOrEmpty(Pt)) return Pt;
            return En ?? Pt ?? "";
        }

        public static LText From(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            var t = new LText();
            object v;
            if (d.TryGetValue("en", out v)) t.En = v as string;
            if (d.TryGetValue("pt", out v)) t.Pt = v as string;
            return t;
        }
    }

    public class Item
    {
        public string Id;
        public LText Title;
        public LText Desc;
        public string[] Commands;
        public string RadioGroup;
        public Group Group;
    }

    public class Group
    {
        public string Id;
        public LText Title;
        public LText Desc;
        public bool Single;
        public bool Exclusive;
        public List<Item> Items = new List<Item>();
        public Category Category;
    }

    public class Category
    {
        public string Id;
        public LText Title;
        public List<Group> Groups = new List<Group>();
    }

    public class Catalog
    {
        public List<Category> Categories = new List<Category>();
        public Dictionary<string, List<string>> Presets = new Dictionary<string, List<string>>();
        public Dictionary<string, Item> ById = new Dictionary<string, Item>();
        public List<Item> AllItems = new List<Item>();

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v as string : null;
        }

        static object[] Arr(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? (v as object[] ?? new object[0]) : new object[0];
        }

        // winget package ids look like "Publisher.Package"; anything else is refused so it can never
        // inject shell syntax into the generated script.
        static readonly Regex WingetId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$");

        public static Catalog LoadEmbedded()
        {
            var cat = Parse(ReadResource("catalog.json"));
            AddApps(cat, ReadResource("apps.json"));
            return cat;
        }

        static string ReadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                return r.ReadToEnd();
            }
        }

        // Adds the "Install apps" category (winget). Runs after the tweaks, so apps install last.
        public static void AddApps(Catalog cat, string json)
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            foreach (Dictionary<string, object> c in Arr(root, "categories"))
            {
                var category = new Category { Id = Str(c, "id"), Title = LText.From(c["title"]) };
                foreach (Dictionary<string, object> g in Arr(c, "groups"))
                {
                    object v;
                    var group = new Group
                    {
                        Id = Str(g, "id"),
                        Title = LText.From(g["title"]),
                        Desc = g.TryGetValue("desc", out v) ? LText.From(v) : null,
                        Category = category
                    };
                    foreach (Dictionary<string, object> a in Arr(g, "items"))
                    {
                        string wid = Str(a, "id");
                        string name = Str(a, "name");
                        if (wid == null || !WingetId.IsMatch(wid) || string.IsNullOrEmpty(name)) continue;
                        var item = new Item
                        {
                            Id = "app:" + wid,
                            Title = new LText { En = name, Pt = name },
                            Desc = new LText { En = "winget: " + wid, Pt = "winget: " + wid },
                            Commands = InstallCommands(name, wid),
                            Group = group
                        };
                        if (cat.ById.ContainsKey(item.Id)) continue;
                        group.Items.Add(item);
                        cat.ById[item.Id] = item;
                        cat.AllItems.Add(item);
                    }
                    if (group.Items.Count > 0) category.Groups.Add(group);
                }
                if (category.Groups.Count > 0) cat.Categories.Add(category);
            }
        }

        static string[] InstallCommands(string name, string id)
        {
            return new[]
            {
                "Write-Host '-- Installing " + name.Replace("'", "''") + "' -ForegroundColor Magenta",
                "winget install --id " + id + " --exact --source winget --silent --accept-package-agreements --accept-source-agreements",
                "if ($LASTEXITCODE -ne 0) { Write-Host \"   winget exit code $LASTEXITCODE (already installed, or failed)\" -ForegroundColor Yellow }"
            };
        }

        // Tweak items only (apps excluded), for counts shown in the UI.
        public int TweakCount { get { return AllItems.Count(i => !i.Id.StartsWith("app:")); } }
        public int TweakCategoryCount { get { return Categories.Count(c => c.Id != "apps"); } }

        public static Catalog Parse(string json)
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            var root = (Dictionary<string, object>)js.DeserializeObject(json);
            var cat = new Catalog();

            foreach (Dictionary<string, object> c in Arr(root, "categories"))
            {
                var category = new Category { Id = Str(c, "id"), Title = LText.From(c["title"]) };
                foreach (Dictionary<string, object> g in Arr(c, "groups"))
                {
                    object v;
                    var group = new Group
                    {
                        Id = Str(g, "id"),
                        Title = g.TryGetValue("title", out v) ? LText.From(v) : null,
                        Desc = g.TryGetValue("desc", out v) ? LText.From(v) : null,
                        Single = g.TryGetValue("single", out v) && v is bool && (bool)v,
                        Exclusive = g.TryGetValue("exclusive", out v) && v is bool && (bool)v,
                        Category = category
                    };
                    foreach (Dictionary<string, object> i in Arr(g, "items"))
                    {
                        var item = new Item
                        {
                            Id = Str(i, "id"),
                            Title = i.TryGetValue("title", out v) ? LText.From(v) : null,
                            Desc = i.TryGetValue("desc", out v) ? LText.From(v) : null,
                            RadioGroup = Str(i, "radioGroup"),
                            Commands = Arr(i, "commands").Select(x => (string)x).ToArray(),
                            Group = group
                        };
                        if (item.Title == null) item.Title = new LText { En = item.Id };
                        if (cat.ById.ContainsKey(item.Id)) continue;
                        group.Items.Add(item);
                        cat.ById[item.Id] = item;
                        cat.AllItems.Add(item);
                    }
                    if (group.Title == null) group.Title = group.Items.Count > 0 ? group.Items[0].Title : new LText { En = group.Id };
                    if (group.Items.Count > 0) category.Groups.Add(group);
                }
                cat.Categories.Add(category);
            }

            object p;
            if (root.TryGetValue("presets", out p))
            {
                foreach (var kv in (Dictionary<string, object>)p)
                    cat.Presets[kv.Key] = ((object[])kv.Value).Select(x => (string)x).ToList();
            }
            return cat;
        }
    }

    // ---------------------------------------------------------------- settings

    public class Settings
    {
        public string Language = "pt";        // pt | en
        public string Theme = "system";       // system | light | dark
        public bool RestorePoint = true;
        public string RunMode = "integrated"; // integrated | console
        public bool ConfirmRun = true;
        public string OnFinish = "ask";       // ask | explorer | nothing
        public bool SaveLogs = true;
        public bool ShowCommands = false;
        public bool RememberSelection = true;
        public List<string> Selection = new List<string>();

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deblowin"); }
        }

        // Generated scripts run as administrator, so they live where only administrators can write.
        // (A per-user folder would let any standard-user process swap the script before it runs.)
        public static string RunsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Deblowin", "runs"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8));
                object v;
                if (d.TryGetValue("language", out v) && v is string) s.Language = (string)v;
                if (d.TryGetValue("theme", out v) && v is string) s.Theme = (string)v;
                if (d.TryGetValue("restorePoint", out v) && v is bool) s.RestorePoint = (bool)v;
                if (d.TryGetValue("runMode", out v) && v is string) s.RunMode = (string)v;
                if (d.TryGetValue("confirmRun", out v) && v is bool) s.ConfirmRun = (bool)v;
                if (d.TryGetValue("onFinish", out v) && v is string) s.OnFinish = (string)v;
                if (d.TryGetValue("saveLogs", out v) && v is bool) s.SaveLogs = (bool)v;
                if (d.TryGetValue("showCommands", out v) && v is bool) s.ShowCommands = (bool)v;
                if (d.TryGetValue("rememberSelection", out v) && v is bool) s.RememberSelection = (bool)v;
                if (d.TryGetValue("selection", out v) && v is object[])
                    s.Selection = ((object[])v).OfType<string>().ToList();
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var d = new Dictionary<string, object>
                {
                    { "language", Language }, { "theme", Theme }, { "restorePoint", RestorePoint },
                    { "runMode", RunMode }, { "confirmRun", ConfirmRun }, { "onFinish", OnFinish },
                    { "saveLogs", SaveLogs }, { "showCommands", ShowCommands },
                    { "rememberSelection", RememberSelection },
                    { "selection", RememberSelection ? Selection : new List<string>() }
                };
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- script builder

    public static class ScriptBuilder
    {
        public const string RestoreId = "restorepoint";

        public static List<Item> Ordered(Catalog cat, ICollection<string> selected, bool forceRestore)
        {
            var list = cat.AllItems.Where(i => selected.Contains(i.Id) && i.Id != RestoreId).ToList();
            Item rp;
            if ((forceRestore || selected.Contains(RestoreId)) && cat.ById.TryGetValue(RestoreId, out rp) && list.Count > 0)
                list.Insert(0, rp);
            return list;
        }

        public static string Build(Catalog cat, ICollection<string> selected, Settings s, bool forConsole)
        {
            var items = Ordered(cat, selected, s.RestorePoint);
            var sb = new StringBuilder();
            sb.AppendLine("# Script generated by Deblowin - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine("try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }");
            sb.AppendLine("if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Write-Host 'This script requires administrator privileges.' -ForegroundColor Red; exit 5 }");
            if (items.Any(i => i.Id.StartsWith("app:")))
            {
                sb.AppendLine("if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { Write-Host 'winget not found. Install App Installer from the Microsoft Store, then run again.' -ForegroundColor Red }");
            }
            sb.AppendLine();

            foreach (var it in items)
            {
                sb.AppendLine("# [" + it.Id + "] " + it.Title.En);
                if (it.Id == RestoreId)
                    sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore\" /v \"SystemRestorePointCreationFrequency\" /t REG_DWORD /d 0 /f | Out-Null");
                foreach (var c in it.Commands) sb.AppendLine(c);
                sb.AppendLine();
            }

            sb.AppendLine("Write-Host '== Done ==' -ForegroundColor Green");
            if (forConsole)
            {
                if (s.OnFinish == "ask")
                    sb.AppendLine("do { Write-Host 'Restart required. Do you want to restart now? (Y/N)' -ForegroundColor Magenta; $response = Read-Host } until ($response -match '^(y|yes|n|no|s|sim)$'); if ($response -match '^(y|yes|s|sim)$') { Restart-Computer -Force } else { Stop-Process -Name explorer -Force; Start-Process explorer.exe }");
                else if (s.OnFinish == "explorer")
                    sb.AppendLine("Stop-Process -Name explorer -Force; Start-Process explorer.exe");
                sb.AppendLine("pause");
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- helpers

    public static class Sys
    {
        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // Directory that only Administrators and SYSTEM can write to (no inherited ACEs).
        public static void EnsureAdminOnlyDir(string path)
        {
            Directory.CreateDirectory(path);
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(sec);
        }

        public static bool SystemUsesDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static string WindowsBuild()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = k.GetValue("ProductName") as string ?? "Windows";
                    string build = k.GetValue("CurrentBuild") as string ?? "?";
                    int b;
                    // Windows 11 still reports "Windows 10" in ProductName.
                    if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    return name + " (build " + build + ")";
                }
            }
            catch { return "Windows"; }
        }
    }

    // ---------------------------------------------------------------- UI strings

    public static class L
    {
        public static string Lang = "pt";

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            { "review", new[] { "Review & run", "Revisar e executar" } },
            { "settings", new[] { "Settings", "Configurações" } },
            { "about", new[] { "About", "Sobre" } },
            { "search", new[] { "Search tweaks…", "Buscar ajustes…" } },
            { "results", new[] { "Search results", "Resultados da busca" } },
            { "noresults", new[] { "No tweak matches your search.", "Nenhum ajuste encontrado." } },
            { "selected", new[] { "{0} selected", "{0} selecionado(s)" } },
            { "presets", new[] { "Preset:", "Preset:" } },
            { "basic", new[] { "Basic", "Básico" } },
            { "strict", new[] { "Strict", "Rigoroso" } },
            { "extreme", new[] { "Extreme", "Extremo" } },
            { "clear", new[] { "Clear", "Limpar" } },
            { "run", new[] { "Run", "Executar" } },
            { "copy", new[] { "Copy", "Copiar" } },
            { "copied", new[] { "Script copied to clipboard.", "Script copiado." } },
            { "save", new[] { "Save .ps1", "Salvar .ps1" } },
            { "import", new[] { "Import selection", "Importar seleção" } },
            { "export", new[] { "Export selection", "Exportar seleção" } },
            { "cancel", new[] { "Cancel", "Cancelar" } },
            { "back", new[] { "Back", "Voltar" } },
            { "none", new[] { "None (don't change)", "Nenhum (não alterar)" } },
            { "all", new[] { "All", "Todos" } },
            { "nothing", new[] { "Nothing selected yet. Pick tweaks in the categories on the left or apply a preset.", "Nada selecionado ainda. Escolha ajustes nas categorias à esquerda ou aplique um preset." } },
            { "reviewdesc", new[] { "These tweaks will run, in this order. Check the script before running it.", "Estes ajustes serão executados, nesta ordem. Confira o script antes de executar." } },
            { "script", new[] { "Generated script", "Script gerado" } },
            { "showscript", new[] { "Show script", "Mostrar script" } },
            { "hidescript", new[] { "Hide script", "Ocultar script" } },
            { "running", new[] { "Running…", "Executando…" } },
            { "finished", new[] { "Finished. Exit code {0}.", "Concluído. Código de saída {0}." } },
            { "canceled", new[] { "Canceled.", "Cancelado." } },
            { "confirmtitle", new[] { "Confirm", "Confirmar" } },
            { "confirmrun", new[] { "Run {0} tweak(s) now?\n\nSome changes (like removing apps) cannot be undone automatically.", "Executar {0} ajuste(s) agora?\n\nAlgumas mudanças (como remover apps) não podem ser desfeitas automaticamente." } },
            { "confirmrp", new[] { "A restore point will be created first.", "Um ponto de restauração será criado antes." } },
            { "confirmnorp", new[] { "Warning: restore point creation is OFF.", "Atenção: a criação de ponto de restauração está DESLIGADA." } },
            { "restartq", new[] { "Done. Some tweaks need a restart.\n\nRestart now?\n(No = only restart Explorer)", "Concluído. Alguns ajustes exigem reinício.\n\nReiniciar agora?\n(Não = só reiniciar o Explorer)" } },
            { "notadmin", new[] { "Deblowin is not running as administrator. Tweaks will fail. Restart the app as administrator.", "O Deblowin não está rodando como administrador. Os ajustes vão falhar. Abra o app como administrador." } },
            { "relaunch", new[] { "Restart as admin", "Reabrir como admin" } },
            { "logsaved", new[] { "Log saved: {0}", "Log salvo: {0}" } },
            { "openlogs", new[] { "Open logs folder", "Abrir pasta de logs" } },
            { "imported", new[] { "{0} tweak(s) imported.", "{0} ajuste(s) importado(s)." } },
            { "badfile", new[] { "Could not read this file.", "Não foi possível ler este arquivo." } },
            { "items", new[] { "{0}/{1}", "{0}/{1}" } },
            // settings
            { "s.general", new[] { "General", "Geral" } },
            { "s.lang", new[] { "Language", "Idioma" } },
            { "s.theme", new[] { "Theme", "Tema" } },
            { "s.theme.system", new[] { "System", "Sistema" } },
            { "s.theme.light", new[] { "Light", "Claro" } },
            { "s.theme.dark", new[] { "Dark", "Escuro" } },
            { "s.exec", new[] { "Execution", "Execução" } },
            { "s.rp", new[] { "Create a restore point before running", "Criar ponto de restauração antes de executar" } },
            { "s.rp.d", new[] { "Recommended. Lets you roll back with System Restore.", "Recomendado. Permite voltar atrás pela Restauração do Sistema." } },
            { "s.confirm", new[] { "Ask for confirmation before running", "Pedir confirmação antes de executar" } },
            { "s.mode", new[] { "Run in", "Executar em" } },
            { "s.mode.integrated", new[] { "This window (with log)", "Esta janela (com log)" } },
            { "s.mode.console", new[] { "PowerShell console", "Console do PowerShell" } },
            { "s.finish", new[] { "When finished", "Ao terminar" } },
            { "s.finish.ask", new[] { "Ask to restart", "Perguntar se reinicia" } },
            { "s.finish.explorer", new[] { "Restart Explorer", "Reiniciar o Explorer" } },
            { "s.finish.nothing", new[] { "Do nothing", "Não fazer nada" } },
            { "s.logs", new[] { "Save execution logs", "Salvar logs da execução" } },
            { "s.interface", new[] { "Interface", "Interface" } },
            { "s.cmds", new[] { "Show commands on hover", "Mostrar comandos ao passar o mouse" } },
            { "s.cmds.d", new[] { "Shows the PowerShell behind each tweak in a tooltip.", "Mostra o PowerShell de cada ajuste numa dica." } },
            { "s.remember", new[] { "Remember selection between sessions", "Lembrar seleção entre sessões" } },
            { "s.data", new[] { "Selection", "Seleção" } },
            // about
            { "a.desc", new[] { "Lightweight tool to debloat and tune Windows: remove bloatware, disable telemetry, and apply privacy and performance tweaks.", "Ferramenta leve para limpar e ajustar o Windows: remove bloatware, desativa telemetria e aplica ajustes de privacidade e desempenho." } },
            { "a.credits", new[] { "Free software under the GPL-3.0 license, with no warranty.", "Software livre sob a licença GPL-3.0, sem garantia." } },
            { "a.warn", new[] { "Use at your own risk. Always keep a backup or restore point.", "Use por sua conta e risco. Mantenha sempre um backup ou ponto de restauração." } },
            { "a.tweaks", new[] { "{0} tweaks in {1} categories", "{0} ajustes em {1} categorias" } },
            { "a.source", new[] { "License and notices", "Licença e avisos" } },
        };

        public static string T(string key)
        {
            string[] v;
            if (!S.TryGetValue(key, out v)) return key;
            return Lang == "pt" ? v[1] : v[0];
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
