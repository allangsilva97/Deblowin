// Deblowin - Windows debloat tool. GPL-3.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Deblowin
{
    public class App : Application
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new App();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));

            var settings = Settings.Load();
            string snapshot = null, snapPage = null, snapPreset = null, exportScript = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--snapshot" && i + 1 < args.Length) snapshot = args[++i];
                else if (a == "--export-script" && i + 1 < args.Length) exportScript = args[++i];
                else if (a == "--page" && i + 1 < args.Length) snapPage = args[++i];
                else if (a == "--theme" && i + 1 < args.Length) settings.Theme = args[++i];
                else if (a == "--lang" && i + 1 < args.Length) settings.Language = args[++i];
                else if (a == "--preset" && i + 1 < args.Length) snapPreset = args[++i];
            }

            var catalog = Catalog.LoadEmbedded();
            if (exportScript != null)
            {
                // Headless: write the script for a preset (or the saved selection) and exit.
                var ids = snapPreset != null && catalog.Presets.ContainsKey(snapPreset)
                    ? catalog.Presets[snapPreset]
                    : snapPreset == "all" ? catalog.AllItems.Select(x => x.Id).ToList() : settings.Selection;
                File.WriteAllText(exportScript, ScriptBuilder.Build(catalog, new HashSet<string>(ids), settings, true), new UTF8Encoding(true));
                return 0;
            }

            var w = new MainWindow(catalog, settings, snapshot != null);
            if (snapPreset != null) w.ApplyPreset(snapPreset);
            if (snapPage != null) w.ShowPage(snapPage);
            if (snapshot != null) w.SnapshotOnLoad(snapshot);
            return app.Run(w);
        }
    }

    public class MainWindow : Window
    {
        readonly Catalog cat;
        readonly Settings st;
        readonly HashSet<string> sel = new HashSet<string>();
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly bool snapshotMode;

        // shell
        StackPanel navPanel;
        readonly Dictionary<string, RadioButton> navButtons = new Dictionary<string, RadioButton>();
        TextBlock pageTitle, pageSub, countText;
        TextBox searchBox;
        ScrollViewer scroller;
        Border footer;
        Button reviewBtn;
        string page;
        string lastNonSearchPage;

        // per-page control maps
        readonly Dictionary<string, List<ToggleButton>> itemControls = new Dictionary<string, List<ToggleButton>>();
        readonly Dictionary<string, List<RadioButton>> noneRadios = new Dictionary<string, List<RadioButton>>();
        readonly Dictionary<Group, CheckBox> masters = new Dictionary<Group, CheckBox>();
        readonly Dictionary<Group, TextBlock> summaries = new Dictionary<Group, TextBlock>();
        bool syncing;
        int radioToken;

        // run state
        Process proc;
        bool running, finished;
        TextBox logBox;
        ProgressBar progress;
        TextBlock runStatus;
        StringBuilder log = new StringBuilder();
        int stepsDone, stepsTotal;
        List<Item> runItems;
        bool showScript;

        public MainWindow(Catalog catalog, Settings settings, bool snapshot)
        {
            cat = catalog;
            st = settings;
            snapshotMode = snapshot;
            L.Lang = st.Language;
            foreach (var id in st.Selection) if (cat.ById.ContainsKey(id)) sel.Add(id);

            Title = "Deblowin";
            Width = 1100;
            Height = 720;
            MinWidth = 880;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "BgBrush");
            if (snapshot)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -20000;
                ShowInTaskbar = false;
            }

            ApplyTheme();
            page = cat.Categories[0].Id;
            BuildShell();

            SourceInitialized += delegate { ApplyTitleBar(); };
            Closing += OnClosing;
            PreviewKeyDown += OnKey;
        }

        // ============================================================ theme

        bool IsDark
        {
            get { return st.Theme == "dark" || (st.Theme == "system" && Sys.SystemUsesDark()); }
        }

        void ApplyTheme()
        {
            bool dark = IsDark;
            var r = Application.Current.Resources;
            Action<string, string> set = (k, hex) =>
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                r[k] = b;
            };
            if (dark)
            {
                set("BgBrush", "#17191D"); set("SidebarBrush", "#1C1F24"); set("SurfaceBrush", "#23272E");
                set("Surface2Brush", "#2A2F37"); set("HoverBrush", "#2F343D"); set("BorderBrush", "#333943");
                set("TextBrush", "#E8EAED"); set("SubTextBrush", "#9AA3AF"); set("AccentBrush", "#4C8DFF");
                set("AccentSoftBrush", "#1D2D4B"); set("AccentTextBrush", "#FFFFFF");
                set("WarnBrush", "#F5B547"); set("WarnSoftBrush", "#3A2E14");
            }
            else
            {
                set("BgBrush", "#F6F7F9"); set("SidebarBrush", "#EDEFF3"); set("SurfaceBrush", "#FFFFFF");
                set("Surface2Brush", "#F0F2F5"); set("HoverBrush", "#E6E9EE"); set("BorderBrush", "#E1E4E8");
                set("TextBrush", "#1B1F24"); set("SubTextBrush", "#5B6470"); set("AccentBrush", "#2F6FEB");
                set("AccentSoftBrush", "#DCE7FD"); set("AccentTextBrush", "#FFFFFF");
                set("WarnBrush", "#B54708"); set("WarnSoftBrush", "#FEF0C7");
            }
            ApplyTitleBar();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void ApplyTitleBar()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;
                int v = IsDark ? 1 : 0;
                DwmSetWindowAttribute(h, 20, ref v, 4);
            }
            catch { }
        }

        // ============================================================ small builders

        static TextBlock Txt(string s, double size, bool sub, bool bold)
        {
            var t = new TextBlock { Text = s, FontSize = size };
            if (sub) t.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        static Border Card()
        {
            var b = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
            b.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            return b;
        }

        Button Btn(string text, string style, RoutedEventHandler click)
        {
            var b = new Button { Content = text };
            b.SetResourceReference(StyleProperty, style);
            b.Click += click;
            return b;
        }

        static Border Separator()
        {
            var b = new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0) };
            b.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            return b;
        }

        string CmdTip(Item it)
        {
            return string.Join("\n", it.Commands.Where(c => !c.StartsWith("Write-Host")).Take(25)) +
                   (it.Commands.Length > 26 ? "\n…" : "");
        }

        // ============================================================ shell

        void BuildShell()
        {
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- sidebar
            var side = new Border { Padding = new Thickness(12, 18, 12, 12) };
            side.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
            var sideDock = new DockPanel();
            side.Child = sideDock;

            var brand = new StackPanel { Margin = new Thickness(8, 0, 0, 18) };
            DockPanel.SetDock(brand, Dock.Top);
            var logo = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 9, 0) };
            mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var markTxt = Txt("D", 13, false, true);
            markTxt.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            markTxt.HorizontalAlignment = HorizontalAlignment.Center;
            markTxt.VerticalAlignment = VerticalAlignment.Center;
            mark.Child = markTxt;
            logo.Children.Add(mark);
            logo.Children.Add(Txt("Deblowin", 17, false, true));
            brand.Children.Add(logo);
            sideDock.Children.Add(brand);

            var bottom = new StackPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            bottom.Children.Add(NavButton("settings", L.T("settings")));
            bottom.Children.Add(NavButton("about", L.T("about")));
            sideDock.Children.Add(bottom);

            navPanel = new StackPanel();
            foreach (var c in cat.Categories) navPanel.Children.Add(NavButton(c.Id, c.Title.Get(st.Language)));
            var sep = new Border { Height = 1, Margin = new Thickness(8, 10, 8, 10) };
            sep.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            navPanel.Children.Add(sep);
            navPanel.Children.Add(NavButton("review", L.T("review")));
            sideDock.Children.Add(navPanel);
            root.Children.Add(side);

            // ---- main
            var main = new Grid();
            Grid.SetColumn(main, 1);
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition());
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(28, 20, 28, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pageTitle = Txt("", 22, false, true);
            pageSub = Txt("", 12.5, true, false);
            pageSub.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(pageTitle);
            titles.Children.Add(pageSub);
            header.Children.Add(titles);

            var searchGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(searchGrid, 1);
            searchBox = new TextBox();
            searchBox.SetResourceReference(StyleProperty, "Input");
            var placeholder = Txt(L.T("search"), 13, true, false);
            placeholder.IsHitTestVisible = false;
            placeholder.Margin = new Thickness(10, 0, 0, 0);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            searchBox.TextChanged += delegate
            {
                placeholder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                OnSearch();
            };
            searchGrid.Children.Add(searchBox);
            searchGrid.Children.Add(placeholder);
            header.Children.Add(searchGrid);
            main.Children.Add(header);

            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 0, 22, 16), Focusable = false };
            Grid.SetRow(scroller, 1);
            main.Children.Add(scroller);

            footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 10, 28, 10) };
            footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            footer.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
            Grid.SetRow(footer, 2);
            var fd = new DockPanel { LastChildFill = false };
            footer.Child = fd;
            var presetsLbl = Txt(L.T("presets"), 13, true, false);
            presetsLbl.VerticalAlignment = VerticalAlignment.Center;
            presetsLbl.Margin = new Thickness(0, 0, 4, 0);
            fd.Children.Add(presetsLbl);
            foreach (var p in new[] { "basic", "strict", "extreme" })
            {
                string key = p;
                var b = Btn(L.T(p), "Btn", delegate { ApplyPreset(key); });
                b.ToolTip = cat.Presets.ContainsKey(p) ? cat.Presets[p].Count + " tweaks" : null;
                fd.Children.Add(b);
            }
            fd.Children.Add(Btn(L.T("clear"), "GhostBtn", delegate { sel.Clear(); RefreshControls(); }));

            reviewBtn = Btn(L.T("review") + "  →", "PrimaryBtn", delegate { ShowPage("review"); });
            DockPanel.SetDock(reviewBtn, Dock.Right);
            fd.Children.Add(reviewBtn);
            countText = Txt("", 13, true, false);
            countText.VerticalAlignment = VerticalAlignment.Center;
            countText.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(countText, Dock.Right);
            fd.Children.Add(countText);
            main.Children.Add(footer);

            root.Children.Add(main);
            Content = root;
            ShowPage(page);
        }

        RadioButton NavButton(string id, string text)
        {
            var rb = new RadioButton { Content = text, GroupName = "nav" };
            rb.SetResourceReference(StyleProperty, "Nav");
            rb.Checked += delegate { if (!syncing && page != id) { searchBox.Text = ""; ShowPage(id); } };
            navButtons[id] = rb;
            return rb;
        }

        // ============================================================ pages

        public void ShowPage(string id)
        {
            page = id;
            if (id != "search") lastNonSearchPage = id;
            itemControls.Clear();
            noneRadios.Clear();
            masters.Clear();
            summaries.Clear();

            syncing = true;
            foreach (var kv in navButtons) kv.Value.IsChecked = kv.Key == id;
            syncing = false;

            var body = new StackPanel { MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Stretch };
            var category = cat.Categories.FirstOrDefault(c => c.Id == id);
            if (category != null)
            {
                pageTitle.Text = category.Title.Get(st.Language);
                int n = category.Groups.Sum(g => g.Items.Count);
                pageSub.Text = n + " " + (st.Language == "pt" ? "ajustes" : "tweaks");
                foreach (var g in category.Groups) body.Children.Add(GroupCard(g));
            }
            else if (id == "search") BuildSearch(body);
            else if (id == "review") BuildReview(body);
            else if (id == "settings") BuildSettings(body);
            else if (id == "about") BuildAbout(body);

            scroller.Content = body;
            scroller.ScrollToTop();
            reviewBtn.Visibility = id == "review" ? Visibility.Collapsed : Visibility.Visible;
            RefreshControls();
        }

        UIElement GroupCard(Group g)
        {
            var card = Card();
            if (g.Single || g.Items.Count == 1)
            {
                card.Child = ItemRow(g.Items[0], null, false);
                return card;
            }

            var stack = new StackPanel();
            card.Child = stack;
            bool isOpen = expanded.Contains(g.Id);

            // header
            var head = new Border { Padding = new Thickness(16, 12, 16, 12), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(Txt(g.Title.Get(st.Language), 14, false, true));
            if (g.Desc != null && !string.IsNullOrEmpty(g.Desc.Get(st.Language)))
                ht.Children.Add(Txt(g.Desc.Get(st.Language), 12.5, true, false));
            hg.Children.Add(ht);

            var summary = Txt("", 12.5, true, false);
            summary.VerticalAlignment = VerticalAlignment.Center;
            summary.Margin = new Thickness(12, 0, 12, 0);
            summary.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(summary, 1);
            hg.Children.Add(summary);
            summaries[g] = summary;

            bool radio = g.Items.All(i => i.RadioGroup != null);
            if (!radio)
            {
                var master = new CheckBox { ToolTip = L.T("all") };
                master.SetResourceReference(StyleProperty, "Switch");
                master.Click += delegate
                {
                    bool all = g.Items.All(i => sel.Contains(i.Id));
                    foreach (var i in g.Items) SetSelected(i, !all, false);
                    RefreshControls();
                };
                Grid.SetColumn(master, 2);
                hg.Children.Add(master);
                masters[g] = master;
            }

            var chevron = Txt(isOpen ? "" : "", 11, true, false);
            chevron.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(chevron, 3);
            hg.Children.Add(chevron);
            head.Child = hg;
            stack.Children.Add(head);

            var panel = new StackPanel { Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed };
            stack.Children.Add(panel);
            head.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (masters.ContainsKey(g) && masters[g].IsMouseOver) return;
                bool open = panel.Visibility != Visibility.Visible;
                panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = open ? "" : "";
                if (open) expanded.Add(g.Id); else expanded.Remove(g.Id);
            };
            head.MouseEnter += delegate { head.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); };
            head.MouseLeave += delegate { head.Background = Brushes.Transparent; };

            panel.Children.Add(Separator());
            if (radio)
            {
                var rp = new StackPanel { Margin = new Thickness(6, 6, 6, 6) };
                string token = "r" + (radioToken++);
                foreach (var rg in g.Items.Select(i => i.RadioGroup).Distinct())
                {
                    string rgName = rg;
                    var none = new RadioButton { Content = Txt(L.T("none"), 13, true, false), GroupName = token + rg };
                    none.SetResourceReference(StyleProperty, "Radio");
                    none.Checked += delegate
                    {
                        if (syncing) return;
                        foreach (var i in g.Items.Where(x => x.RadioGroup == rgName)) sel.Remove(i.Id);
                        RefreshControls();
                    };
                    rp.Children.Add(none);
                    if (!noneRadios.ContainsKey(rg)) noneRadios[rg] = new List<RadioButton>();
                    noneRadios[rg].Add(none);

                    foreach (var it in g.Items.Where(x => x.RadioGroup == rgName))
                    {
                        var item = it;
                        var content = new StackPanel();
                        content.Children.Add(Txt(it.Title.Get(st.Language), 13, false, false));
                        if (it.Desc != null) content.Children.Add(Txt(it.Desc.Get(st.Language), 12, true, false));
                        var rb = new RadioButton { Content = content, GroupName = token + rg };
                        rb.SetResourceReference(StyleProperty, "Radio");
                        if (st.ShowCommands) rb.ToolTip = CmdTip(it);
                        rb.Checked += delegate { if (!syncing) { SetSelected(item, true, false); RefreshControls(); } };
                        Track(it.Id, rb);
                        rp.Children.Add(rb);
                    }
                }
                panel.Children.Add(rp);
            }
            else
            {
                bool first = true;
                foreach (var it in g.Items)
                {
                    if (!first) panel.Children.Add(Separator());
                    first = false;
                    panel.Children.Add(ItemRow(it, null, true));
                }
            }
            return card;
        }

        void Track(string id, ToggleButton tb)
        {
            List<ToggleButton> list;
            if (!itemControls.TryGetValue(id, out list)) itemControls[id] = list = new List<ToggleButton>();
            list.Add(tb);
        }

        UIElement ItemRow(Item it, string path, bool nested)
        {
            var row = new Border
            {
                Padding = nested ? new Thickness(28, 10, 16, 10) : new Thickness(16, 12, 16, 12),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            if (path != null)
            {
                var p = Txt(path, 11.5, true, false);
                p.Margin = new Thickness(0, 0, 0, 1);
                text.Children.Add(p);
            }
            text.Children.Add(Txt(it.Title.Get(st.Language), nested ? 13 : 14, false, !nested));
            if (it.Desc != null && !string.IsNullOrEmpty(it.Desc.Get(st.Language)))
                text.Children.Add(Txt(it.Desc.Get(st.Language), 12.5, true, false));
            g.Children.Add(text);

            var cb = new CheckBox();
            cb.SetResourceReference(StyleProperty, "Switch");
            AutomationName(cb, it.Title.Get(st.Language));
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            row.Child = g;
            if (st.ShowCommands) row.ToolTip = CmdTip(it);

            cb.Checked += delegate { if (!syncing) { SetSelected(it, true, false); RefreshControls(); } };
            cb.Unchecked += delegate { if (!syncing) { SetSelected(it, false, false); RefreshControls(); } };
            row.MouseLeftButtonUp += delegate { if (!cb.IsMouseOver) cb.IsChecked = cb.IsChecked != true; };
            row.MouseEnter += delegate { row.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            Track(it.Id, cb);
            return row;
        }

        static void AutomationName(DependencyObject o, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(o, name);
        }

        void SetSelected(Item it, bool on, bool refresh)
        {
            if (on)
            {
                if (it.RadioGroup != null)
                    foreach (var other in cat.AllItems.Where(x => x.RadioGroup == it.RadioGroup)) sel.Remove(other.Id);
                sel.Add(it.Id);
            }
            else sel.Remove(it.Id);
            if (refresh) RefreshControls();
        }

        void RefreshControls()
        {
            syncing = true;
            foreach (var kv in itemControls)
            {
                bool on = sel.Contains(kv.Key);
                foreach (var tb in kv.Value) if (tb.IsChecked != on) tb.IsChecked = on;
            }
            foreach (var kv in noneRadios)
            {
                string rg = kv.Key;
                bool any = cat.AllItems.Any(i => i.RadioGroup == rg && sel.Contains(i.Id));
                foreach (var rb in kv.Value) rb.IsChecked = !any;
            }
            foreach (var kv in masters)
            {
                int n = kv.Key.Items.Count(i => sel.Contains(i.Id));
                kv.Value.IsChecked = n == 0 ? (bool?)false : n == kv.Key.Items.Count ? (bool?)true : null;
            }
            foreach (var kv in summaries)
            {
                var g = kv.Key;
                var chosen = g.Items.Where(i => sel.Contains(i.Id)).ToList();
                if (g.Items.All(i => i.RadioGroup != null))
                    kv.Value.Text = chosen.Count == 0 ? "—" : string.Join(", ", chosen.Select(i => i.Title.Get(st.Language)));
                else
                    kv.Value.Text = L.T("items", chosen.Count, g.Items.Count);
            }
            syncing = false;

            int count = sel.Count;
            countText.Text = L.T("selected", count);
            reviewBtn.IsEnabled = count > 0;
            foreach (var c in cat.Categories)
            {
                RadioButton nb;
                if (!navButtons.TryGetValue(c.Id, out nb)) continue;
                int k = c.Groups.SelectMany(g => g.Items).Count(i => sel.Contains(i.Id));
                nb.Content = NavContent(c.Title.Get(st.Language), k);
            }
            RadioButton rv;
            if (navButtons.TryGetValue("review", out rv)) rv.Content = NavContent(L.T("review"), count);
        }

        static UIElement NavContent(string text, int badge)
        {
            var d = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 8, 0) };
            if (badge > 0)
            {
                var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center };
                b.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                var t = new TextBlock { Text = badge.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold };
                t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                b.Child = t;
                DockPanel.SetDock(b, Dock.Right);
                d.Children.Add(b);
            }
            d.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            return d;
        }

        // ---------------------------------------------------------------- search

        void OnSearch()
        {
            string q = searchBox.Text.Trim();
            if (q.Length >= 2) { if (page != "search") ShowPage("search"); else ShowPage("search"); }
            else if (page == "search") ShowPage(lastNonSearchPage ?? cat.Categories[0].Id);
        }

        void BuildSearch(StackPanel body)
        {
            string q = searchBox.Text.Trim().ToLowerInvariant();
            pageTitle.Text = L.T("results");
            var hits = cat.AllItems.Where(i =>
                i.Title.Get(st.Language).ToLowerInvariant().Contains(q) ||
                (i.Title.En ?? "").ToLowerInvariant().Contains(q) ||
                (i.Desc != null && i.Desc.Get(st.Language).ToLowerInvariant().Contains(q)) ||
                i.Group.Title.Get(st.Language).ToLowerInvariant().Contains(q) ||
                i.Id.ToLowerInvariant().Contains(q)).ToList();
            pageSub.Text = "\"" + searchBox.Text.Trim() + "\" · " + hits.Count;
            if (hits.Count == 0)
            {
                body.Children.Add(Txt(L.T("noresults"), 14, true, false));
                return;
            }
            foreach (var it in hits)
            {
                var card = Card();
                string path = it.Group.Category.Title.Get(st.Language) +
                              (it.Group.Items.Count > 1 ? "  ›  " + it.Group.Title.Get(st.Language) : "");
                card.Child = ItemRow(it, path, false);
                body.Children.Add(card);
            }
        }

        // ---------------------------------------------------------------- review / run

        void BuildReview(StackPanel body)
        {
            pageTitle.Text = L.T("review");
            if (running || finished) { BuildRunPanel(body); return; }

            var items = ScriptBuilder.Ordered(cat, sel, st.RestorePoint);
            pageSub.Text = L.T("selected", sel.Count);

            if (!Sys.IsAdmin())
            {
                var warn = Card();
                warn.SetResourceReference(Border.BackgroundProperty, "WarnSoftBrush");
                warn.Padding = new Thickness(16, 12, 16, 12);
                var wd = new DockPanel();
                var rel = Btn(L.T("relaunch"), "Btn", delegate { RelaunchAsAdmin(); });
                DockPanel.SetDock(rel, Dock.Right);
                wd.Children.Add(rel);
                var wt = Txt(L.T("notadmin"), 13, false, false);
                wt.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                wt.VerticalAlignment = VerticalAlignment.Center;
                wd.Children.Add(wt);
                warn.Child = wd;
                body.Children.Add(warn);
            }

            if (items.Count == 0)
            {
                body.Children.Add(Txt(L.T("nothing"), 14, true, false));
                return;
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var run = Btn("▶  " + L.T("run"), "PrimaryBtn", delegate { Run(); });
            run.Margin = new Thickness(0);
            actions.Children.Add(run);
            actions.Children.Add(Btn(L.T("copy"), "Btn", delegate
            {
                try { Clipboard.SetText(ScriptBuilder.Build(cat, sel, st, true)); pageSub.Text = L.T("copied"); } catch { }
            }));
            actions.Children.Add(Btn(L.T("save"), "Btn", delegate { SaveScript(); }));
            actions.Children.Add(Btn(showScript ? L.T("hidescript") : L.T("showscript"), "GhostBtn", delegate { showScript = !showScript; ShowPage("review"); }));
            body.Children.Add(actions);

            var hint = Txt(L.T("reviewdesc"), 12.5, true, false);
            hint.Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(hint);

            if (showScript)
            {
                var box = MonoBox(ScriptBuilder.Build(cat, sel, st, st.RunMode == "console"));
                box.Height = 320;
                box.Margin = new Thickness(0, 0, 0, 12);
                body.Children.Add(box);
            }

            foreach (var grp in items.GroupBy(i => i.Group.Category))
            {
                var card = Card();
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var h = Txt(grp.Key.Title.Get(st.Language) + "  ·  " + grp.Count(), 12.5, true, true);
                h.Margin = new Thickness(16, 8, 16, 6);
                sp.Children.Add(h);
                foreach (var it in grp)
                {
                    var item = it;
                    var d = new DockPanel { Margin = new Thickness(16, 2, 8, 2) };
                    if (it.Id != ScriptBuilder.RestoreId || !st.RestorePoint)
                    {
                        var x = Btn("", "GhostBtn", delegate { sel.Remove(item.Id); ShowPage("review"); });
                        x.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                        x.FontSize = 10;
                        x.ToolTip = st.Language == "pt" ? "Remover" : "Remove";
                        DockPanel.SetDock(x, Dock.Right);
                        d.Children.Add(x);
                    }
                    string label = it.Title.Get(st.Language);
                    if (it.Group.Items.Count > 1) label = it.Group.Title.Get(st.Language) + " › " + label;
                    var t = Txt(label, 13, false, false);
                    t.VerticalAlignment = VerticalAlignment.Center;
                    if (st.ShowCommands) t.ToolTip = CmdTip(it);
                    d.Children.Add(t);
                    sp.Children.Add(d);
                }
                card.Child = sp;
                body.Children.Add(card);
            }
        }

        TextBox MonoBox(string text)
        {
            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true
            };
            box.SetResourceReference(StyleProperty, "Input");
            box.SetResourceReference(TextBox.BackgroundProperty, "Surface2Brush");
            return box;
        }

        void BuildRunPanel(StackPanel body)
        {
            pageSub.Text = runItems == null ? "" : L.T("selected", runItems.Count);
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btns, Dock.Right);
            if (running) btns.Children.Add(Btn(L.T("cancel"), "Btn", delegate { CancelRun(); }));
            else
            {
                btns.Children.Add(Btn(L.T("openlogs"), "GhostBtn", delegate { OpenLogs(); }));
                btns.Children.Add(Btn(L.T("back"), "PrimaryBtn", delegate { finished = false; ShowPage("review"); }));
            }
            top.Children.Add(btns);
            runStatus = Txt(running ? L.T("running") : "", 14, false, true);
            runStatus.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(runStatus);
            body.Children.Add(top);

            progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = Math.Max(1, stepsTotal), Value = stepsDone, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 12) };
            progress.SetResourceReference(ForegroundProperty, "AccentBrush");
            progress.SetResourceReference(BackgroundProperty, "Surface2Brush");
            body.Children.Add(progress);

            logBox = MonoBox(log.ToString());
            logBox.Height = Math.Max(300, ActualHeight - 300);
            logBox.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(logBox);
            logBox.ScrollToEnd();
            UpdateRunStatus();
        }

        void UpdateRunStatus()
        {
            if (runStatus == null) return;
            if (running) runStatus.Text = L.T("running") + "  " + stepsDone + "/" + stepsTotal;
            if (progress != null) { progress.Maximum = Math.Max(1, stepsTotal); progress.Value = stepsDone; }
        }

        void AppendLog(string line)
        {
            log.AppendLine(line);
            if (logBox != null)
            {
                logBox.AppendText(line + "\n");
                logBox.ScrollToEnd();
            }
        }

        void Run()
        {
            var items = ScriptBuilder.Ordered(cat, sel, st.RestorePoint);
            if (items.Count == 0 || running) return;

            if (st.ConfirmRun)
            {
                string msg = L.T("confirmrun", items.Count) + "\n\n" + (st.RestorePoint ? L.T("confirmrp") : L.T("confirmnorp"));
                if (MessageBox.Show(this, msg, "Deblowin", MessageBoxButton.YesNo, st.RestorePoint ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            // Not %TEMP%: the "clean temporary files" tweak empties it while the script runs.
            string dir = Path.Combine(Settings.Dir, "runs");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string file = Path.Combine(dir, "run-" + stamp + ".ps1");
            bool console = st.RunMode == "console";
            File.WriteAllText(file, ScriptBuilder.Build(cat, sel, st, console), new UTF8Encoding(true));
            string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

            if (console)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(ps, "-NoProfile -NoLogo -ExecutionPolicy Bypass -File \"" + file + "\"") { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Deblowin"); }
                return;
            }

            runItems = items;
            stepsDone = 0;
            stepsTotal = items.Count;
            log = new StringBuilder();
            running = true;
            finished = false;
            ShowPage("review");
            AppendLog("# Deblowin " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + Sys.WindowsBuild());
            AppendLog("# " + file);
            AppendLog("");

            var psi = new ProcessStartInfo(ps, "-NoProfile -NoLogo -NonInteractive -ExecutionPolicy Bypass -File \"" + file + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler onData = delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string line = e.Data;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (line.StartsWith("-- ")) { stepsDone = Math.Min(stepsTotal, stepsDone + 1); UpdateRunStatus(); }
                    AppendLog(line);
                }));
            };
            proc.OutputDataReceived += onData;
            proc.ErrorDataReceived += onData;
            proc.Exited += delegate
            {
                int code = -1;
                try { proc.WaitForExit(); code = proc.ExitCode; } catch { }
                Dispatcher.BeginInvoke(new Action(delegate { OnRunFinished(code, stamp); }));
            };
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                running = false;
                finished = true;
                AppendLog(ex.Message);
                ShowPage("review");
            }
        }

        bool canceled;

        void CancelRun()
        {
            if (proc == null || !running) return;
            canceled = true;
            try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + proc.Id) { CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        }

        void OnRunFinished(int code, string stamp)
        {
            running = false;
            finished = true;
            AppendLog("");
            AppendLog(canceled ? L.T("canceled") : L.T("finished", code));
            if (!canceled) stepsDone = stepsTotal;
            if (st.SaveLogs)
            {
                try
                {
                    string dir = Path.Combine(Settings.Dir, "logs");
                    Directory.CreateDirectory(dir);
                    string f = Path.Combine(dir, "run-" + stamp + ".log");
                    File.WriteAllText(f, log.ToString(), new UTF8Encoding(true));
                    AppendLog(L.T("logsaved", f));
                }
                catch { }
            }
            bool wasCanceled = canceled;
            canceled = false;
            ShowPage("review");
            if (runStatus != null) runStatus.Text = wasCanceled ? L.T("canceled") : L.T("finished", code);
            if (wasCanceled) return;

            if (st.OnFinish == "ask")
            {
                var r = MessageBox.Show(this, L.T("restartq"), "Deblowin", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes) Shell("shutdown", "/r /t 5");
                else if (r == MessageBoxResult.No) RestartExplorer();
            }
            else if (st.OnFinish == "explorer") RestartExplorer();
        }

        static void Shell(string exe, string args)
        {
            try { Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        void RestartExplorer()
        {
            // Killing explorer lets Windows restart it un-elevated (AutoRestartShell).
            foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += delegate
            {
                t.Stop();
                if (Process.GetProcessesByName("explorer").Length == 0) Shell("explorer.exe", "");
            };
            t.Start();
        }

        void OpenLogs()
        {
            string dir = Path.Combine(Settings.Dir, "logs");
            Directory.CreateDirectory(dir);
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        void SaveScript()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "deblowin.ps1", Filter = "PowerShell (*.ps1)|*.ps1" };
            if (dlg.ShowDialog(this) == true)
                File.WriteAllText(dlg.FileName, ScriptBuilder.Build(cat, sel, st, true), new UTF8Encoding(true));
        }

        void RelaunchAsAdmin()
        {
            try
            {
                st.Selection = sel.ToList();
                st.Save();
                Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = true, Verb = "runas" });
                Application.Current.Shutdown();
            }
            catch (Win32Exception) { }
        }

        // ---------------------------------------------------------------- presets / import / export

        public void ApplyPreset(string name)
        {
            List<string> ids;
            if (!cat.Presets.TryGetValue(name, out ids)) return;
            sel.Clear();
            foreach (var id in ids)
            {
                Item it;
                if (cat.ById.TryGetValue(id, out it)) SetSelected(it, true, false);
            }
            if (page == "review") ShowPage("review"); else RefreshControls();
        }

        void Export()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "deblowin-config.json", Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            var d = new Dictionary<string, object> { { "app", "Deblowin" }, { "selected", sel.ToList() } };
            File.WriteAllText(dlg.FileName, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
        }

        void Import()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json|*.*|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                object o = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(dlg.FileName));
                var ids = new List<string>();
                var dict = o as Dictionary<string, object>;
                if (dict != null && dict.ContainsKey("selected") && dict["selected"] is object[])
                    ids.AddRange(((object[])dict["selected"]).OfType<string>());
                else if (o is object[])
                    ids.AddRange(((object[])o).OfType<string>());
                else if (dict != null) // { "id": true, ... }
                    ids.AddRange(dict.Where(kv => kv.Value is bool && (bool)kv.Value).Select(kv => kv.Key));
                sel.Clear();
                int n = 0;
                foreach (var id in ids)
                {
                    Item it;
                    if (cat.ById.TryGetValue(id, out it)) { SetSelected(it, true, false); n++; }
                }
                ShowPage(page);
                MessageBox.Show(this, L.T("imported", n), "Deblowin");
            }
            catch { MessageBox.Show(this, L.T("badfile"), "Deblowin", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // ---------------------------------------------------------------- settings page

        void BuildSettings(StackPanel body)
        {
            pageTitle.Text = L.T("settings");
            pageSub.Text = Settings.Dir;

            body.Children.Add(Section(L.T("s.general"),
                ChoiceRow(L.T("s.lang"), null, new[] { "pt", "en" }, new[] { "Português", "English" }, st.Language,
                    v => { st.Language = v; L.Lang = v; st.Save(); BuildShell(); ShowPage("settings"); }),
                ChoiceRow(L.T("s.theme"), null, new[] { "system", "light", "dark" },
                    new[] { L.T("s.theme.system"), L.T("s.theme.light"), L.T("s.theme.dark") }, st.Theme,
                    v => { st.Theme = v; st.Save(); ApplyTheme(); })));

            body.Children.Add(Section(L.T("s.exec"),
                SwitchRow(L.T("s.rp"), L.T("s.rp.d"), st.RestorePoint, v => { st.RestorePoint = v; st.Save(); }),
                SwitchRow(L.T("s.confirm"), null, st.ConfirmRun, v => { st.ConfirmRun = v; st.Save(); }),
                ChoiceRow(L.T("s.mode"), null, new[] { "integrated", "console" },
                    new[] { L.T("s.mode.integrated"), L.T("s.mode.console") }, st.RunMode, v => { st.RunMode = v; st.Save(); }),
                ChoiceRow(L.T("s.finish"), null, new[] { "ask", "explorer", "nothing" },
                    new[] { L.T("s.finish.ask"), L.T("s.finish.explorer"), L.T("s.finish.nothing") }, st.OnFinish,
                    v => { st.OnFinish = v; st.Save(); }),
                SwitchRow(L.T("s.logs"), Path.Combine(Settings.Dir, "logs"), st.SaveLogs, v => { st.SaveLogs = v; st.Save(); })));

            body.Children.Add(Section(L.T("s.interface"),
                SwitchRow(L.T("s.cmds"), L.T("s.cmds.d"), st.ShowCommands, v => { st.ShowCommands = v; st.Save(); }),
                SwitchRow(L.T("s.remember"), null, st.RememberSelection, v => { st.RememberSelection = v; st.Save(); })));

            var io = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 12) };
            var imp = Btn(L.T("import"), "Btn", delegate { Import(); });
            imp.Margin = new Thickness(0);
            io.Children.Add(imp);
            io.Children.Add(Btn(L.T("export"), "Btn", delegate { Export(); }));
            body.Children.Add(Section(L.T("s.data"), io));
        }

        UIElement Section(string title, params UIElement[] rows)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var h = Txt(title, 12.5, true, true);
            h.Margin = new Thickness(2, 4, 0, 6);
            wrap.Children.Add(h);
            var card = Card();
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sp.Children.Add(Separator());
                sp.Children.Add(rows[i]);
            }
            card.Child = sp;
            wrap.Children.Add(card);
            return wrap;
        }

        UIElement SettingRow(string title, string desc, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            t.Children.Add(Txt(title, 13.5, false, false));
            if (desc != null) t.Children.Add(Txt(desc, 12, true, false));
            g.Children.Add(t);
            Grid.SetColumn((UIElement)control, 1);
            g.Children.Add(control);
            return g;
        }

        UIElement SwitchRow(string title, string desc, bool value, Action<bool> onChange)
        {
            var cb = new CheckBox { IsChecked = value };
            cb.SetResourceReference(StyleProperty, "Switch");
            AutomationName(cb, title);
            cb.Click += delegate { onChange(cb.IsChecked == true); };
            return SettingRow(title, desc, cb);
        }

        UIElement ChoiceRow(string title, string desc, string[] values, string[] labels, string current, Action<string> onChange)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string group = "c" + (radioToken++);
            for (int i = 0; i < values.Length; i++)
            {
                string v = values[i];
                var rb = new RadioButton { Content = labels[i], GroupName = group, IsChecked = v == current };
                rb.SetResourceReference(StyleProperty, "Segment");
                rb.Checked += delegate { if (!syncing) onChange(v); };
                sp.Children.Add(rb);
            }
            return SettingRow(title, desc, sp);
        }

        // ---------------------------------------------------------------- about page

        void BuildAbout(StackPanel body)
        {
            pageTitle.Text = L.T("about");
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            pageSub.Text = "Deblowin " + v.Major + "." + v.Minor + "." + v.Build;
            var card = Card();
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(Txt(L.T("a.desc"), 14, false, false));
            var stats = Txt(L.T("a.tweaks", cat.AllItems.Count, cat.Categories.Count) + "  ·  " + Sys.WindowsBuild() + "  ·  " + (Sys.IsAdmin() ? "admin" : "user"), 12.5, true, false);
            stats.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(stats);
            var cr = Txt(L.T("a.credits"), 13, false, false);
            cr.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(cr);
            var w = Txt(L.T("a.warn"), 13, false, false);
            w.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            w.Margin = new Thickness(0, 10, 0, 14);
            sp.Children.Add(w);
            var link = Btn(L.T("a.source"), "Btn", delegate { ShowNotice(); });
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(0);
            sp.Children.Add(link);
            card.Child = sp;
            body.Children.Add(card);
        }

        void ShowNotice()
        {
            string text = "";
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NOTICE"))
                using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            }
            catch { }
            text += "\nGPL-3.0: https://www.gnu.org/licenses/gpl-3.0.html";
            MessageBox.Show(this, text, L.T("a.source"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ============================================================ window events

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && searchBox.IsKeyboardFocused) { searchBox.Text = ""; e.Handled = true; }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (running)
            {
                var r = MessageBox.Show(this, L.T("running") + "\n\n" + L.T("cancel") + "?", "Deblowin", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
                CancelRun();
            }
            if (snapshotMode) return;
            st.Selection = sel.ToList();
            st.Save();
        }

        // ============================================================ snapshot (docs / testing)

        public void SnapshotOnLoad(string path)
        {
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
