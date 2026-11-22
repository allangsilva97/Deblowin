// Deblowin installer / uninstaller. GPL-3.0. See LICENSE.
//
//   Deblowin-Setup.exe                 interactive install
//   Deblowin-Setup.exe /S [/D=<dir>]   silent install
//   uninstall.exe /uninstall [/S]      (copied into the install folder)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeblowinSetup
{
    static class T
    {
        public static bool Pt = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";
        public static string S(string en, string pt) { return Pt ? pt : en; }
    }

    static class Installer
    {
        public const string AppName = "Deblowin";
        public const string ExeName = "Deblowin.exe";
        public const string Publisher = "allangsilva97";
        public const string Url = "https://github.com/allangsilva97/Deblowin";
        const string UninstKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Deblowin";

        public static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string DefaultDir
        {
            get
            {
                string existing = InstalledDir();
                if (existing != null) return existing;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
            }
        }

        public static string InstalledDir()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(UninstKey))
                    return k == null ? null : k.GetValue("InstallLocation") as string;
            }
            catch { return null; }
        }

        static string StartMenuLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"); }
        }

        static string DesktopLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"); }
        }

        static void Extract(string resource, string target)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var f = File.Create(target)) s.CopyTo(f);
        }

        static void StopRunning(string dir)
        {
            foreach (var p in Process.GetProcessesByName("Deblowin"))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); }
                }
                catch { }
            }
        }

        static void Shortcut(string lnk, string target, string dir)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                sc.WorkingDirectory = dir;
                sc.IconLocation = target + ",0";
                sc.Description = T.S("Debloat and tune Windows", "Limpe e ajuste o Windows");
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        public static void Install(string dir, bool desktop, Action<int, string> report)
        {
            report(5, T.S("Preparing…", "Preparando…"));
            Directory.CreateDirectory(dir);
            StopRunning(dir);

            report(25, T.S("Copying files…", "Copiando arquivos…"));
            string exe = Path.Combine(dir, ExeName);
            Extract("payload.exe", exe);
            Extract("LICENSE", Path.Combine(dir, "LICENSE.txt"));
            Extract("NOTICE", Path.Combine(dir, "NOTICE.txt"));
            string uninst = Path.Combine(dir, "uninstall.exe");
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninst, true);

            report(60, T.S("Creating shortcuts…", "Criando atalhos…"));
            Shortcut(StartMenuLnk, exe, dir);
            if (desktop) Shortcut(DesktopLnk, exe, dir);
            else if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk);

            report(85, T.S("Registering…", "Registrando…"));
            long kb = new DirectoryInfo(dir).GetFiles().Sum(f => f.Length) / 1024;
            using (var k = Registry.LocalMachine.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", Publisher);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /S");
                k.SetValue("URLInfoAbout", Url);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            report(100, T.S("Done", "Concluído"));
        }

        public static void Uninstall(string dir, bool removeData, Action<int, string> report)
        {
            report(10, T.S("Closing Deblowin…", "Fechando o Deblowin…"));
            StopRunning(dir);

            report(40, T.S("Removing files…", "Removendo arquivos…"));
            foreach (var f in new[] { ExeName, "LICENSE.txt", "NOTICE.txt" })
            {
                try { File.Delete(Path.Combine(dir, f)); } catch { }
            }
            try { File.Delete(StartMenuLnk); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            if (removeData)
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
                try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch { }
            }

            report(80, T.S("Unregistering…", "Removendo registro…"));
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstKey, false); } catch { }

            // uninstall.exe cannot delete itself while running: let cmd do it after we exit.
            // "rd" without /s only removes the folder if it is empty.
            string uninst = Path.Combine(dir, "uninstall.exe");
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + uninst + "\" & rd \"" + dir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
            catch { }
            report(100, T.S("Done", "Concluído"));
        }
    }

    public class SetupApp : Application
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            string dir = args.Where(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(3).Trim('"')).FirstOrDefault();
            string snapshot = null, page = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--snapshot" && i + 1 < args.Length) snapshot = args[++i];
                else if (args[i] == "--page" && i + 1 < args.Length) page = args[++i];
            }

            if (uninstall && dir == null)
                dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null) dir = Installer.DefaultDir;

            if (silent)
            {
                try
                {
                    Action<int, string> nop = delegate { };
                    if (uninstall) Installer.Uninstall(dir, false, nop);
                    else Installer.Install(dir, true, nop);
                    return 0;
                }
                catch { return 1; }
            }

            var app = new SetupApp();
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
            var w = new SetupWindow(uninstall || page == "uninstall", dir);
            if (snapshot != null) w.Snapshot(snapshot, page);
            return app.Run(w);
        }
    }

    public class SetupWindow : Window
    {
        readonly bool uninstallMode;
        string dir;
        Border body;
        bool busy;

        public SetupWindow(bool uninstall, string installDir)
        {
            uninstallMode = uninstall;
            dir = installDir;
            Title = uninstall ? T.S("Uninstall Deblowin", "Desinstalar o Deblowin") : T.S("Deblowin Setup", "Instalação do Deblowin");
            Width = 540;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            ApplyTheme();

            var root = new DockPanel();
            root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
            var header = new Border { Padding = new Thickness(28, 24, 28, 20) };
            header.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
            DockPanel.SetDock(header, Dock.Top);
            var hs = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) };
            try
            {
                var dec = BitmapDecoder.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                icon.Source = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
            catch { }
            hs.Children.Add(icon);
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(Txt("Deblowin", 20, false, true));
            ht.Children.Add(Txt(T.S("Version ", "Versão ") + Installer.Version, 12.5, true, false));
            hs.Children.Add(ht);
            header.Child = hs;
            root.Children.Add(header);

            body = new Border { Padding = new Thickness(28, 20, 28, 22) };
            root.Children.Add(body);
            Content = root;

            if (uninstallMode) ShowUninstall(); else ShowWelcome();
            SourceInitialized += delegate { DarkTitle(); };
            Closing += (s, e) => { if (busy) e.Cancel = true; };
        }

        // ------------------------------------------------------------ theme

        static bool Dark()
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

        void ApplyTheme()
        {
            var r = Application.Current.Resources;
            string[] keys = { "BgBrush", "SidebarBrush", "SurfaceBrush", "Surface2Brush", "HoverBrush", "BorderBrush", "TextBrush", "SubTextBrush", "AccentBrush", "AccentSoftBrush", "AccentTextBrush", "WarnBrush", "WarnSoftBrush" };
            string[] dark = { "#17191D", "#1C1F24", "#23272E", "#2A2F37", "#2F343D", "#333943", "#E8EAED", "#9AA3AF", "#4C8DFF", "#1D2D4B", "#FFFFFF", "#F5B547", "#3A2E14" };
            string[] light = { "#F6F7F9", "#EDEFF3", "#FFFFFF", "#F0F2F5", "#E6E9EE", "#E1E4E8", "#1B1F24", "#5B6470", "#2F6FEB", "#DCE7FD", "#FFFFFF", "#B54708", "#FEF0C7" };
            var pal = Dark() ? dark : light;
            for (int i = 0; i < keys.Length; i++)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pal[i]));
                b.Freeze();
                r[keys[i]] = b;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void DarkTitle()
        {
            try { int v = Dark() ? 1 : 0; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref v, 4); } catch { }
        }

        // ------------------------------------------------------------ helpers

        static TextBlock Txt(string s, double size, bool sub, bool bold)
        {
            var t = new TextBlock { Text = s, FontSize = size };
            if (sub) t.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        Button Btn(string text, string style, RoutedEventHandler click)
        {
            var b = new Button { Content = text, MinWidth = 96 };
            b.SetResourceReference(StyleProperty, style);
            if (click != null) b.Click += click;
            return b;
        }

        UIElement SwitchRow(string text, bool value, Action<bool> changed)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Txt(text, 13, false, false);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            var cb = new CheckBox { IsChecked = value };
            cb.SetResourceReference(StyleProperty, "Switch");
            System.Windows.Automation.AutomationProperties.SetName(cb, text);
            cb.Checked += delegate { changed(true); };
            cb.Unchecked += delegate { changed(false); };
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            g.MouseLeftButtonUp += delegate { if (!cb.IsMouseOver) cb.IsChecked = cb.IsChecked != true; };
            return g;
        }

        static DockPanel Buttons(params Button[] buttons)
        {
            var d = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 20, 0, 0) };
            foreach (var b in buttons.Reverse()) { DockPanel.SetDock(b, Dock.Right); d.Children.Add(b); }
            return d;
        }

        // ------------------------------------------------------------ install pages

        bool desktop = true;

        void ShowWelcome()
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(T.S(
                "Lightweight tool to debloat and tune Windows: remove bloatware, disable telemetry, and apply privacy and performance tweaks.",
                "Ferramenta leve para limpar e ajustar o Windows: remove bloatware, desativa telemetria e aplica ajustes de privacidade e desempenho."), 13, false, false));

            var lbl = Txt(T.S("Install folder", "Pasta de instalação"), 12.5, true, true);
            lbl.Margin = new Thickness(0, 18, 0, 6);
            sp.Children.Add(lbl);
            var pathRow = new DockPanel();
            var change = Btn(T.S("Change…", "Alterar…"), "Btn", null);
            DockPanel.SetDock(change, Dock.Right);
            pathRow.Children.Add(change);
            var box = new TextBox { Text = dir, IsReadOnly = true };
            box.SetResourceReference(StyleProperty, "Input");
            pathRow.Children.Add(box);
            change.Click += delegate
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = Path.GetDirectoryName(dir) ?? dir })
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    string p = dlg.SelectedPath;
                    // Always install into a dedicated "Deblowin" folder so uninstall never touches other files.
                    if (!Path.GetFileName(p.TrimEnd('\\')).Equals(Installer.AppName, StringComparison.OrdinalIgnoreCase))
                        p = Path.Combine(p, Installer.AppName);
                    dir = p;
                    box.Text = p;
                }
            };
            sp.Children.Add(pathRow);

            var opts = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            opts.Children.Add(SwitchRow(T.S("Create desktop shortcut", "Criar atalho na área de trabalho"), desktop, v => desktop = v));
            sp.Children.Add(opts);

            var lic = Txt(T.S(
                "Deblowin is free software under the GPL-3.0 license and comes with no warranty. Some tweaks cannot be undone automatically.",
                "O Deblowin é software livre sob a licença GPL-3.0 e não tem garantia. Alguns ajustes não podem ser desfeitos automaticamente."), 12, true, false);
            lic.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(lic);

            sp.Children.Add(Buttons(
                Btn(T.S("Cancel", "Cancelar"), "Btn", delegate { Close(); }),
                Btn(T.S("Install", "Instalar"), "PrimaryBtn", delegate { RunTask(false); })));
            body.Child = sp;
        }

        TextBlock status;
        ProgressBar bar;

        void ShowProgress(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(title, 15, false, true));
            status = Txt("", 12.5, true, false);
            status.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(status);
            bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, BorderThickness = new Thickness(0) };
            bar.SetResourceReference(ForegroundProperty, "AccentBrush");
            bar.SetResourceReference(BackgroundProperty, "Surface2Brush");
            sp.Children.Add(bar);
            sp.Children.Add(new Border { Height = 60 });
            body.Child = sp;
        }

        void RunTask(bool uninstall)
        {
            busy = true;
            ShowProgress(uninstall ? T.S("Uninstalling…", "Desinstalando…") : T.S("Installing…", "Instalando…"));
            string target = dir;
            bool removeData = this.removeData, makeDesktop = desktop;
            Action<int, string> report = (p, s) => Dispatcher.BeginInvoke(new Action(delegate { bar.Value = p; status.Text = s; }));
            var th = new Thread(delegate()
            {
                string error = null;
                try
                {
                    if (uninstall) Installer.Uninstall(target, removeData, report);
                    else Installer.Install(target, makeDesktop, report);
                }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    if (error != null) ShowError(error);
                    else if (uninstall) ShowUninstalled();
                    else ShowDone();
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        bool launch = true;

        void ShowDone()
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(T.S("Deblowin is installed", "O Deblowin foi instalado"), 16, false, true));
            var d = Txt(T.S("Find it in the Start menu. To remove it, use Settings › Apps › Installed apps.",
                            "Ele está no Menu Iniciar. Para remover, use Configurações › Aplicativos › Aplicativos instalados."), 13, true, false);
            d.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(d);
            sp.Children.Add(SwitchRow(T.S("Open Deblowin now", "Abrir o Deblowin agora"), launch, v => launch = v));
            sp.Children.Add(Buttons(Btn(T.S("Finish", "Concluir"), "PrimaryBtn", delegate
            {
                if (launch)
                {
                    try { Process.Start(new ProcessStartInfo(Path.Combine(dir, Installer.ExeName)) { WorkingDirectory = dir }); } catch { }
                }
                Close();
            })));
            body.Child = sp;
        }

        void ShowError(string message)
        {
            var sp = new StackPanel();
            var h = Txt(T.S("Something went wrong", "Algo deu errado"), 16, false, true);
            h.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            sp.Children.Add(h);
            var m = Txt(message, 13, false, false);
            m.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(m);
            sp.Children.Add(Buttons(Btn(T.S("Close", "Fechar"), "PrimaryBtn", delegate { Close(); })));
            body.Child = sp;
        }

        // ------------------------------------------------------------ uninstall pages

        bool removeData;

        void ShowUninstall()
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(T.S("Remove Deblowin from this computer?", "Remover o Deblowin deste computador?"), 15, false, true));
            var d = Txt(T.S("Tweaks already applied to Windows stay as they are.", "Os ajustes já aplicados ao Windows continuam como estão."), 13, true, false);
            d.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(d);
            sp.Children.Add(SwitchRow(T.S("Also delete settings, logs and saved scripts", "Apagar também configurações, logs e scripts salvos"), removeData, v => removeData = v));
            sp.Children.Add(Buttons(
                Btn(T.S("Cancel", "Cancelar"), "Btn", delegate { Close(); }),
                Btn(T.S("Uninstall", "Desinstalar"), "PrimaryBtn", delegate { RunTask(true); })));
            body.Child = sp;
        }

        void ShowUninstalled()
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(T.S("Deblowin was removed", "O Deblowin foi removido"), 16, false, true));
            sp.Children.Add(Buttons(Btn(T.S("Close", "Fechar"), "PrimaryBtn", delegate { Close(); })));
            body.Child = sp;
        }

        // ------------------------------------------------------------ snapshot (docs / testing)

        public void Snapshot(string path, string page)
        {
            Left = -20000;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            if (page == "progress") { ShowProgress(T.S("Installing…", "Instalando…")); bar.Value = 60; status.Text = T.S("Creating shortcuts…", "Criando atalhos…"); }
            else if (page == "done") ShowDone();
            ContentRendered += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    var el = (FrameworkElement)Content;
                    var dpi = VisualTreeHelper.GetDpi(el);
                    var bmp = new RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY),
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    bmp.Render(el);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var f = File.Create(path)) enc.Save(f);
                    Close();
                }));
            };
        }
    }
}
