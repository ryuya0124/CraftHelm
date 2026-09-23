using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftHarbor.Core;
using CraftHarbor.Desktop;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "CraftHarbor.UiTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = new Application(); app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CraftHarbor;component/Styles.xaml") }); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            var store = new HarborStore(root); var p = store.Add("Harbor Survival"); p.Engine = "fabric"; p.Port = 25572; store.Save();
            var window = new MainWindow(root);
            var navigate = typeof(MainWindow).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var content = (FrameworkElement)window.Content;
            void Layout() { content.Measure(new Size(1240, 840)); content.Arrange(new Rect(0, 0, 1240, 840)); content.UpdateLayout(); }
            for (int pass = 0; pass < 2; pass++)
                foreach (var key in new[] { "overview", "console", "backups", "server-settings", "launch", "resources", "advanced", "install", "import", "properties", "manage", "mods", "modsearch", "presets", "modpacks", "automodpack", "modsettings", "files", "settings", "java", "system", "network", "help", "updates", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    Console.WriteLine($"PASS UI navigation/layout {key} round {pass + 1}");
                }
            var creation = new ServerCreationWindow((kind, _) => Task.FromResult(kind == "fabric" ? new[] { "1.20.1" } : new[] { "1.21.1" }));
            creation.Show(); creation.UpdateLayout();
            awaitVersions(creation.LoadVersionsAsync());
            var creationControls = Descendants((DependencyObject)creation.Content);
            var newEngine = creationControls.OfType<ComboBox>().Single(x => x.Name == "NewServerEngine");
            var newVersion = creationControls.OfType<TextBox>().Single(x => x.Name == "NewServerVersion");
            if (creation.Version != "1.21.1") throw new Exception("Creation version not initialized");
            newEngine.SelectedItem = "fabric"; awaitVersions(creation.LoadVersionsAsync());
            if (creation.Version != "1.20.1" || creation.Engine != "fabric") throw new Exception("Creation options ignored selected loader");
            newVersion.Text = "1.19.4";
            if (creation.Version != "1.19.4") throw new Exception("Manual version fallback failed");
            creation.UpdateLayout();
            if (args.Length > 0) SaveImage(creation, Path.Combine(Path.GetDirectoryName(args[0])!, "new-server.png"));
            creation.Close();
            navigate.Invoke(window, ["system"]); Layout();
            if (Descendants(content).OfType<ProgressBar>().Count(x => x.Name == "SystemMeter") < 2) throw new Exception("PC dashboard meters missing");
            navigate.Invoke(window, ["updates"]); Layout();
            if (Descendants(content).OfType<Button>().Single(x => x.Name == "RestartUpdate").Visibility != Visibility.Collapsed) throw new Exception("Update restart shown before verified download");
            Console.WriteLine("PASS creation loader/version, system dashboard, update button gating");
            navigate.Invoke(window, ["launch"]); Layout();
            var engine = Descendants(content).OfType<ComboBox>().Single(x => x.Name == "ServerEngine");
            var fabric = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "FabricSettings");
            var versionButton = Descendants(content).OfType<Button>().Single(x => x.Name == "ServerVersions");
            var saveButton = Descendants(content).OfType<Button>().Single(x => x.Content?.ToString() == "設定を保存");
            foreach (var kind in new[] { "vanilla", "paper", "folia", "forge", "neoforge", "quilt", "custom", "fabric" })
            {
                engine.SelectedItem = kind; Layout();
                if ((fabric.Visibility == Visibility.Visible) != (kind == "fabric")) throw new Exception("Incorrect Fabric fields for " + kind);
                if (versionButton.IsEnabled != (kind != "custom")) throw new Exception("Incorrect version selector for " + kind);
                Descendants(fabric).OfType<TextBox>().Single().Text = "0.16.0";
                saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = new HarborStore(root).Profiles.Single();
                if (saved.Engine != kind || saved.LoaderVersion != (kind == "fabric" ? "0.16.0" : "")) throw new Exception("Incorrect persisted engine/loader for " + kind);
                navigate.Invoke(window, ["launch"]); Layout();
                engine = Descendants(content).OfType<ComboBox>().Single(x => x.Name == "ServerEngine");
                fabric = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "FabricSettings");
                versionButton = Descendants(content).OfType<Button>().Single(x => x.Name == "ServerVersions");
                saveButton = Descendants(content).OfType<Button>().Single(x => x.Content?.ToString() == "設定を保存");
                if (engine.SelectedItem?.ToString() != kind || (fabric.Visibility == Visibility.Visible) != (kind == "fabric")) throw new Exception("Incorrect reopened engine for " + kind);
                Console.WriteLine("PASS UI engine switch/save/reopen " + kind);
            }
            foreach (var appearance in new[] { "Light", "Dark" })
            {
                Theme.Apply(appearance, true);
                Theme.Load(root);
                if (Theme.Appearance != appearance) throw new Exception("Theme preference did not persist");
                AssertColor("demo.json", "{\"name\":\"world\",\"enabled\":true,\"maxPlayers\":12}", "\"name\"", "Key");
                AssertColor("demo.json", "{\"name\":\"world\",\"enabled\":true,\"maxPlayers\":12}", "world", "String");
                AssertColor("demo.json", "{\"name\":\"world\",\"enabled\":true,\"maxPlayers\":12}", "true", "Keyword");
                AssertColor("demo.json", "{\"name\":\"world\",\"enabled\":true,\"maxPlayers\":12}", "12", "Number");
                AssertColor("demo.json5", "{\"url\":\"https://example.com\"}", "example", "String");
                AssertColor("demo.jsonc", "// note", "note", "Comment");
                AssertColor("demo.toml", "rate = 12.5 # note", "rate", "Key");
                AssertColor("demo.toml", "rate = 12.5 # note", "12.5", "Number");
                AssertColor("demo.toml", "rate = 12.5 # note", "note", "Comment");
                AssertColor("demo.toml", "[display]", "display", "Section");
                AssertColor("demo.toml", "name = \"a#b\"", "a#b", "String");
                var keyColor = ConfigurationSyntax.ForPath("demo.json")!.GetNamedColor("Key")?.Foreground?.GetColor(null);
                if (keyColor != (Color)ColorConverter.ConvertFromString(appearance == "Dark" ? "#83C9FF" : "#075A9B")) throw new Exception("Syntax colors do not follow " + appearance + " theme");
                var themedServerList = Descendants(content).OfType<ListBox>().Single(x => x.Name == "ServerList");
                themedServerList.ApplyTemplate();
                if (themedServerList.Template.FindName("ServerListSurface", themedServerList) is not Border listSurface || ((SolidColorBrush)listSurface.Background).Color != ((SolidColorBrush)app.Resources["Input"]).Color)
                    throw new Exception("Server list background does not follow " + appearance + " theme");
                foreach (var key in new[] { "overview", "console", "backups", "server-settings", "launch", "resources", "advanced", "install", "import", "properties", "manage", "mods", "modsearch", "presets", "modpacks", "automodpack", "modsettings", "files", "settings", "java", "system", "network", "help", "updates", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    var expected = ((SolidColorBrush)app.Resources["Input"]).Color;
                    foreach (var box in Descendants(content).OfType<ComboBox>())
                    {
                        if (((SolidColorBrush)box.Background).Color != expected) throw new Exception("ComboBox has incorrect theme");
                        if (box.Template.FindName("PART_Popup", box) is not System.Windows.Controls.Primitives.Popup popup || popup.Child is not Border popupBorder || ((SolidColorBrush)popupBorder.Background).Color != expected) throw new Exception("Dropdown has incorrect theme");
                    }
                    if (((SolidColorBrush)((Grid)content).Background).Color != ((SolidColorBrush)app.Resources["Background"]).Color) throw new Exception("Local background did not update");
                    if (args.Length > 0 && key is "launch" or "appearance" or "server-settings" or "mods" or "system" or "updates" or "files") SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, $"{appearance.ToLowerInvariant()}-{key}.png"));
                }
                var contextMenu = Descendants(content).OfType<ListBox>().First().ContextMenu!;
                contextMenu.ApplyTemplate(); contextMenu.Measure(new Size(260, 120)); contextMenu.Arrange(new Rect(0, 0, contextMenu.DesiredSize.Width, contextMenu.DesiredSize.Height)); contextMenu.UpdateLayout();
                if (contextMenu.Template.FindName("MenuSurface", contextMenu) is not Border surface || surface.Child is not ItemsPresenter || ((SolidColorBrush)surface.Background).Color != ((SolidColorBrush)app.Resources["Surface"]).Color) throw new Exception("Server context menu still uses the system icon gutter or incorrect theme");
                foreach (var item in contextMenu.Items.OfType<MenuItem>())
                {
                    item.ApplyTemplate();
                    if (item.Template.FindName("ItemSurface", item) is not Border chrome || ((SolidColorBrush)item.Foreground).Color != ((SolidColorBrush)app.Resources["Ink"]).Color || chrome.Background is not SolidColorBrush brush || brush.Color != Colors.Transparent) throw new Exception("Server menu item still uses system colors or icon gutter");
                }
                if (args.Length > 0) SaveMenuImage(contextMenu, Path.Combine(Path.GetDirectoryName(args[0])!, $"{appearance.ToLowerInvariant()}-server-menu.png"));
                var dialog = HarborDialog.Create(null, "テーマの確認ダイアログ", "確認", MessageBoxButton.YesNo, out var result);
                dialog.ApplyTemplate();
                if (((SolidColorBrush)dialog.Background).Color != ((SolidColorBrush)app.Resources["Background"]).Color || result() != MessageBoxResult.No) throw new Exception("Dialog theme or safe default is incorrect");
                dialog.Close(); Console.WriteLine("PASS theme apply/persist/all pages/dropdown/dialog " + appearance);
            }
            if (window.Icon == null || Theme.Icon.Width != 256) throw new Exception("App icon is missing or low resolution");
            Console.WriteLine("PASS multi-resolution app icon");
            var serverDir = store.ServerDir(p); var autoDir = Path.Combine(serverDir, "automodpack"); Directory.CreateDirectory(autoDir);
            File.WriteAllText(Path.Combine(autoDir, "automodpack-server.json"), "{\"modpackName\":\"before\"}");
            File.WriteAllText(Path.Combine(autoDir, "automodpack-client.json"), "{\"testPrivateData\":true}");
            navigate.Invoke(window, ["files"]); Layout();
            TreeView ConfigTree() => Descendants(content).OfType<TreeView>().Single(x => x.Name == "ConfigFiles");
            TreeViewItem[] ConfigNodes()
            {
                var nodes = new List<TreeViewItem>();
                void Visit(ItemCollection items)
                {
                    foreach (var node in items.OfType<TreeViewItem>()) { nodes.Add(node); Visit(node.Items); }
                }
                Visit(ConfigTree().Items); return nodes.ToArray();
            }
            ConfigurationFileEntry[] ConfigEntries() => ConfigNodes().Select(x => x.Tag).OfType<ConfigurationFileEntry>().ToArray();
            TreeViewItem ConfigFile(string path) => ConfigNodes().Single(x => x.Tag is ConfigurationFileEntry e && e.RelativePath == path);
            TextEditor ConfigEditor() => Descendants(content).OfType<TextEditor>().Single(x => x.Name == "ConfigEditor");
            var configs = ConfigTree();
            var firstFileNode = ConfigNodes().First(x => x.Tag is ConfigurationFileEntry);
            firstFileNode.ApplyTemplate();
            if (firstFileNode.Template.FindName("ItemSurface", firstFileNode) is not Border treeItemSurface || treeItemSurface.BorderThickness != new Thickness(1))
                throw new Exception("Configuration hover outline has no reserved width and can resize its row");
            if (ConfigEditor().FontSize != 15) throw new Exception("Editor reading size did not increase");
            var textView = ConfigEditor().TextArea.TextView;
            textView.EnsureVisualLines();
            if (textView.VisualLines[0].Height < 22) throw new Exception("Editor line spacing did not grow beyond font metrics");
            Console.WriteLine($"INFO editor line height: {textView.VisualLines[0].Height:F1}");
            var zoom = typeof(MainWindow).GetMethod("ZoomEditor", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var originalEditorText = ConfigEditor().Text;
            zoom.Invoke(window, [ConfigEditor(), 120]);
            if (ConfigEditor().FontSize != 16 || ConfigEditor().Text != originalEditorText) throw new Exception("Editor zoom in changed text or failed");
            Layout(); textView.EnsureVisualLines();
            if (textView.VisualLines[0].Height < 24) throw new Exception("Editor line spacing did not follow zoom");
            zoom.Invoke(window, [ConfigEditor(), -120]);
            if (ConfigEditor().FontSize != 15) throw new Exception("Editor zoom out failed");
            for (var i = 0; i < 30; i++) zoom.Invoke(window, [ConfigEditor(), -120]);
            if (ConfigEditor().FontSize != 10) throw new Exception("Editor zoom minimum not enforced");
            for (var i = 0; i < 30; i++) zoom.Invoke(window, [ConfigEditor(), 120]);
            if (ConfigEditor().FontSize != 28) throw new Exception("Editor zoom maximum not enforced");
            for (var i = 0; i < 13; i++) zoom.Invoke(window, [ConfigEditor(), -120]);
            if (ConfigEditor().FontSize != 15) throw new Exception("Editor zoom did not return to its default size");
            if (!ConfigEntries().Any(e => e.RelativePath == Path.Combine("automodpack", "automodpack-server.json")) || ConfigEntries().Any(e => e.RelativePath.EndsWith("automodpack-client.json"))) throw new Exception("AutoModpack configuration scope incorrect");
            var searchFiles = Descendants(content).OfType<TextBox>().Single(x => x.Name == "ConfigSearch");
            searchFiles.Text = "automodpack"; Layout();
            if (ConfigEntries().Length != 1) throw new Exception("Configuration search did not narrow the list");
            if (!ConfigNodes().Single(x => x.Tag?.ToString() == "automodpack").IsExpanded) throw new Exception("Matching configuration folder did not expand during search");
            searchFiles.Clear(); Layout();
            ConfigFile(Path.Combine("automodpack", "automodpack-server.json")).IsSelected = true;
            if (ConfigEditor().SyntaxHighlighting?.Name != "JSON" || !ConfigEditor().ShowLineNumbers) throw new Exception("JSON highlighting or line numbers were not activated");
            if (args.Length > 0) { Layout(); SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, "dark-json.png")); }
            const string updatedAuto = "{\"modpackName\":\"日本語同期テスト\"}\n";
            ConfigEditor().Text = updatedAuto;
            searchFiles.Text = "server.properties"; Layout();
            if (!ConfigEditor().Text.Contains("日本語同期テスト")) throw new Exception("Filtering discarded unsaved editor text");
            searchFiles.Clear(); Layout();
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "ファイルを保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (File.ReadAllText(Path.Combine(autoDir, "automodpack-server.json")) != updatedAuto || !SafeFiles.Files(Path.Combine(root, "file-history")).Any()) throw new Exception("AutoModpack save/history or newline fidelity failed");
            var extraConfigs = new[] { "config/ftb.snbt", "config/mod.json5", "defaultconfigs/create.toml", "Adventure/serverconfig/mod.toml", "kubejs/server_scripts/test.js", "scripts/test.zs" };
            foreach (var relative in extraConfigs) { var file = Path.Combine(serverDir, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, relative.EndsWith(".toml") ? "[display]\nname = \"CraftHelm\"\nenabled = true\nrate = 12.5 # test\n" : "original"); }
            navigate.Invoke(window, ["files"]); Layout(); configs = ConfigTree();
            var groups = ConfigEntries().ToDictionary(e => e.RelativePath, e => e.Category);
            if (groups[Path.Combine("Adventure", "serverconfig", "mod.toml")] != "ワールド" || groups[Path.Combine("scripts", "test.zs")] != "スクリプト" || groups[Path.Combine("automodpack", "automodpack-server.json")] != "AutoModpack") throw new Exception("Configuration folders were grouped incorrectly");
            var category = Descendants(content).OfType<Button>().Single(b => b.Tag?.ToString() == "MOD設定");
            category.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            if (ConfigEntries().Any(e => e.Category != "MOD設定") || ConfigEntries().Length < 2) throw new Exception("Configuration category filter failed");
            Descendants(content).OfType<Button>().Single(b => b.Tag?.ToString() == "すべて").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            if (args.Length > 0) SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, "dark-files.png"));
            if (!ConfigNodes().Any(x => x.Tag?.ToString() == "config") || !ConfigNodes().Any(x => x.Tag?.ToString() == "Adventure/serverconfig")) throw new Exception("Nested configuration folders are missing");
            var folder = ConfigNodes().Single(x => x.Tag?.ToString() == "Adventure");
            folder.IsExpanded = false; Layout();
            if (folder.IsExpanded) throw new Exception("Configuration folder cannot be collapsed");
            searchFiles.Text = "server.properties"; searchFiles.Clear(); Layout();
            if (ConfigNodes().Single(x => x.Tag?.ToString() == "Adventure").IsExpanded) throw new Exception("Collapsed configuration folder reopened after filtering");
            ConfigNodes().Single(x => x.Tag?.ToString() == "Adventure").IsExpanded = true; Layout();
            content.Measure(new Size(980, 700)); content.Arrange(new Rect(0, 0, 980, 700)); content.UpdateLayout();
            if (ConfigEditor().ActualWidth < 200 || configs.ActualWidth < 150) throw new Exception("Configuration browser does not fit minimum window width");
            var smallEditorHeight = ConfigEditor().ActualHeight; var smallTreeHeight = configs.ActualHeight;
            content.Measure(new Size(2200, 1300)); content.Arrange(new Rect(0, 0, 2200, 1300)); content.UpdateLayout();
            if (ConfigEditor().ActualHeight < smallEditorHeight + 300 || configs.ActualHeight < smallTreeHeight + 300 || ConfigEditor().ActualHeight < 650) throw new Exception("Configuration browser did not grow with window height");
            if (configs.ActualWidth < 300) throw new Exception("Configuration tree did not grow with window width");
            content.Measure(new Size(980, 700)); content.Arrange(new Rect(0, 0, 980, 700)); content.UpdateLayout();
            if (ConfigEditor().ActualHeight > smallEditorHeight + 25 || configs.ActualHeight > smallTreeHeight + 25) throw new Exception("Configuration browser did not shrink with window");
            Layout();
            foreach (var relative in extraConfigs)
            {
                var item = relative.Replace('/', Path.DirectorySeparatorChar); if (!ConfigEntries().Any(e => e.RelativePath == item)) throw new Exception("Missing config " + relative);
                ConfigFile(item).IsSelected = true;
                if (args.Length > 0 && relative.EndsWith(".toml")) { Layout(); SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, "dark-toml.png")); }
                ConfigEditor().Text = "edited 日本語";
                if (ConfigEditor().SyntaxHighlighting?.Name != (relative.EndsWith(".toml") ? "TOML" : relative.EndsWith(".js") || relative.EndsWith(".zs") ? "スクリプト" : relative.EndsWith(".json5") ? "JSON" : "設定")) throw new Exception("Wrong syntax colors for " + relative);
                Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "ファイルを保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (File.ReadAllText(Path.Combine(serverDir, relative)) != "edited 日本語") throw new Exception("Save failed " + relative);
            }
            navigate.Invoke(window, ["mods"]); Layout();
            if (Descendants(content).OfType<ListBox>().Single(x => x.Name == "InstalledJars").Height < 400) throw new Exception("Installed MOD list is too small");
            navigate.Invoke(window, ["backups"]); Layout();
            if (!Descendants(content).OfType<ProgressBar>().Any(x => x.Name == "BackupProgress") || !Descendants(content).OfType<Button>().Any(b => b.Content?.ToString() == "保存先を開く")) throw new Exception("Backup progress or destination control is missing");
            var busyField = typeof(MainWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var factory = typeof(MainWindow).GetMethod("Btn", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var opened = false;
            var openDuringBackup = (Button)factory.Invoke(window, ["保存先を開く", (Action)(() => opened = true), false, true])!;
            busyField.SetValue(window, true); openDuringBackup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); busyField.SetValue(window, false);
            if (!opened) throw new Exception("Opening backup destination was blocked while busy");
            navigate.Invoke(window, ["manage"]); Layout();
            if (!Descendants(content).OfType<Button>().Any(b => b.Content?.ToString() == "一覧からのみ削除") || !Descendants(content).OfType<Button>().Any(b => b.Content?.ToString() == "サーバーフォルダもゴミ箱へ移動")) throw new Exception("Selected server deletion choices are missing");
            navigate.Invoke(window, ["presets"]); Layout();
            if (Descendants(content).OfType<CheckBox>().Single(b => b.Content?.ToString()?.StartsWith("現在の設定を維持") == true).IsChecked != true) throw new Exception("Preserve settings must be default");
            Console.WriteLine("PASS extended MOD config UI edits and preservation default");
            File.WriteAllText(Path.Combine(autoDir, "automodpack-server.json"), "{\"DO_NOT_CHANGE_IT\":7,\"modpackName\":\"元の名前\",\"generateModpackOnStart\":false,\"syncedFiles\":[\"mods/**\"],\"extra\":{\"unknown\":12}}" );
            navigate.Invoke(window, ["modsettings"]); Layout();
            if (!Descendants(content).OfType<TextBlock>().Any(t => t.Text == "配布するMOD構成の名前")) throw new Exception("Japanese MOD setting label missing");
            Descendants(content).OfType<TextBox>().Single(t => t.Tag?.ToString() == "modpackName").Text = "日本語の同期構成";
            Descendants(content).OfType<TextBox>().Single(t => t.Tag?.ToString() == "syncedFiles").Text = "mods/**\nconfig/**";
            Descendants(content).OfType<CheckBox>().Single(t => t.Tag?.ToString() == "generateModpackOnStart").IsChecked = true;
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "MOD設定を保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            navigate.Invoke(window, ["overview"]); navigate.Invoke(window, ["modsettings"]); Layout();
            var autoSaved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(autoDir, "automodpack-server.json")))!;
            if (autoSaved["modpackName"]!.ToString() != "日本語の同期構成" || !autoSaved["generateModpackOnStart"]!.GetValue<bool>() || autoSaved["syncedFiles"]!.AsArray().Count != 2 || autoSaved["extra"]!["unknown"]!.GetValue<int>() != 12 || autoSaved["DO_NOT_CHANGE_IT"]!.GetValue<int>() != 7) throw new Exception("MOD settings GUI changed unrelated values or failed save");
            if (Descendants(content).OfType<TextBox>().Single(t => t.Tag?.ToString() == "modpackName").Text != "日本語の同期構成") throw new Exception("MOD settings reopen failed");
            if (args.Length > 0) SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, "japanese-mod-settings.png"));
            var detailLinks = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "ServerSettingsNavigation");
            var detailPanel = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "DetailContent");
            var leftScroll = Descendants(content).OfType<ScrollViewer>().Single(x => ReferenceEquals(x.Content, detailLinks));
            var rightScroll = Descendants(content).OfType<ScrollViewer>().Single(x => ReferenceEquals(x.Content, detailPanel));
            var outerScroll = (ScrollViewer)typeof(MainWindow).GetField("pageScroll", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            if (outerScroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled || rightScroll.ExtentHeight <= rightScroll.ViewportHeight || leftScroll.ActualHeight > outerScroll.ActualHeight + 1) throw new Exception("Settings columns do not scroll independently");
            leftScroll.ScrollToEnd(); Layout(); var leftOffset = leftScroll.VerticalOffset;
            if (leftOffset < 100) throw new Exception("Server settings menu did not scroll");
            rightScroll.ScrollToEnd(); Layout();
            if (Math.Abs(leftScroll.VerticalOffset - leftOffset) > 1) throw new Exception("Scrolling the MOD form moved the left menu");
            if (args.Length > 0) SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, "mod-settings-scrolled.png"));
            detailLinks.Children.OfType<Button>().Single(b => b.Name == "ServerSettingNavmods").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            if (!ReferenceEquals(leftScroll, Descendants(content).OfType<ScrollViewer>().Single(x => ReferenceEquals(x.Content, detailLinks))) || Math.Abs(leftScroll.VerticalOffset - leftOffset) > 1 || rightScroll.VerticalOffset > 1) throw new Exception("Selecting a settings tab reset the left menu or kept the right scroll position");
            navigate.Invoke(window, ["modsettings"]); Layout();
            var links = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "NavigationLinks");
            if (links.Children.OfType<Button>().Count() != 4 || links.Children.OfType<Button>().Any(b => b.Name is "Navappearance" or "Navmods")) throw new Exception("Primary navigation contains detail pages");
            if (Descendants(content).OfType<WrapPanel>().Any(x => x.Name is "NavigationGroups" or "SectionCategories")) throw new Exception("Old stacked navigation remains");
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "アプリ設定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            var appLinks = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "SettingsNavigation");
            if (appLinks.Children.OfType<Button>().Count() != 7 || Descendants(content).OfType<TextBlock>().Any(t => t.Text?.Contains("Minecraft 1.21") == true)) throw new Exception("App settings hub or server-independent breadcrumb is incorrect");
            appLinks.Children.OfType<Button>().Single(b => b.Name == "SettingNavappearance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            if (!Descendants(content).OfType<TextBlock>().Any(t => t.Text == "自分に合った明るさで")) throw new Exception("Appearance setting did not open");
            links.Children.OfType<Button>().Single(b => b.Name == "Navserversettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            var serverLinks = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "ServerSettingsNavigation");
            if (serverLinks.Children.OfType<Button>().Count() != 15 || !serverLinks.Children.OfType<Button>().Any(b => b.Name == "ServerSettingNavmodsettings")) throw new Exception("Server settings sections are incomplete");
            var serverList = Descendants(content).OfType<ListBox>().First();
            if (serverList.ContextMenu?.Items.OfType<MenuItem>().Count() != 2 || !serverList.ContextMenu.Items.OfType<MenuItem>().Any(x => x.Header?.ToString() == "サーバーを削除…")) throw new Exception("Server context menu is missing");
            serverList.ContextMenu.Items.OfType<MenuItem>().Single(x => x.Header?.ToString() == "サーバーを削除…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Layout();
            if (!Descendants(content).OfType<Button>().Any(b => b.Content?.ToString() == "一覧からのみ削除")) throw new Exception("Context menu did not open server deletion choices");
            Descendants(content).OfType<StackPanel>().Single(x => x.Name == "ServerSettingsNavigation").Children.OfType<Button>().Single(b => b.Name == "ServerSettingNavresources").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            var javaInput = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Java実行ファイルの場所");
            if (!javaInput.IsEnabled || Descendants(content).OfType<TextBox>().Any(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Minecraft バージョン")) throw new Exception("Server resource page is not separate");
            Console.WriteLine("PASS Japanese MOD form save/reopen, nested unknown values, arrays and separate page navigation");
            navigate.Invoke(window, ["console"]); Layout();
            foreach (var command in new[] { "automodpack", "automodpack host", "automodpack generate", "automodpack config reload" })
                if (!Descendants(content).OfType<Button>().Any(b => b.Tag?.ToString() == command)) throw new Exception("Missing AutoModpack command");
            Console.WriteLine("PASS AutoModpack config discovery/save/history and console commands");
            var propertiesPath = Path.Combine(serverDir, "server.properties");
            File.WriteAllText(propertiesPath, "# retain\nmotd=before\nmax-players=20\ndifficulty=easy\ngamemode=survival\nwhite-list=false\nserver-port=25572\nrcon.password=hidden\ncustom.setting=keep\n");
            navigate.Invoke(window, ["properties"]); Layout();
            Descendants(content).OfType<TextBox>().Single(x => x.Tag?.ToString() == "motd").Text = "日本語サーバー";
            Descendants(content).OfType<TextBox>().Single(x => x.Tag?.ToString() == "max-players").Text = "8";
            Descendants(content).OfType<TextBox>().Single(x => x.Tag?.ToString() == "server-port").Text = "25572";
            Descendants(content).OfType<CheckBox>().Single(x => x.Tag?.ToString() == "white-list").IsChecked = true;
            Descendants(content).OfType<ComboBox>().Single(x => x.Tag?.ToString() == "difficulty").SelectedValue = "hard";
            if (Descendants(content).OfType<PasswordBox>().Single().Password != "hidden") throw new Exception("Secret editor missing");
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "サーバー設定を保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            navigate.Invoke(window, ["overview"]); navigate.Invoke(window, ["properties"]); Layout();
            var savedProperties = new ServerProperties(File.ReadAllText(propertiesPath));
            if (savedProperties.Values["motd"] != "日本語サーバー" || savedProperties.Values["difficulty"] != "hard" || savedProperties.Values["white-list"] != "true" || savedProperties.Values["custom.setting"] != "keep" || !File.ReadAllText(propertiesPath).StartsWith("# retain") || new HarborStore(root).Profiles.Single().Port != 25572) throw new Exception("GUI properties roundtrip failed");
            if (Descendants(content).OfType<TextBox>().Single(x => x.Tag?.ToString() == "motd").Text != "日本語サーバー") throw new Exception("GUI properties reopen failed");
            File.WriteAllText(propertiesPath, File.ReadAllText(propertiesPath).Replace("difficulty=hard", "difficulty=3").Replace("gamemode=survival", "gamemode=1"));
            navigate.Invoke(window, ["overview"]); navigate.Invoke(window, ["properties"]); Layout();
            var difficulty = Descendants(content).OfType<ComboBox>().Single(x => x.Tag?.ToString() == "difficulty");
            var gamemode = Descendants(content).OfType<ComboBox>().Single(x => x.Tag?.ToString() == "gamemode");
            if (difficulty.Items.Count != 4 || gamemode.Items.Count != 4 || difficulty.SelectedValue?.ToString() != "hard" || gamemode.SelectedValue?.ToString() != "creative") throw new Exception("Legacy numeric choices are duplicated or mislabeled");
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "サーバー設定を保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!File.ReadAllText(propertiesPath).Contains("difficulty=3") || !File.ReadAllText(propertiesPath).Contains("gamemode=1")) throw new Exception("Unchanged legacy property values were rewritten");
            gamemode.SelectedValue = "adventure";
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "サーバー設定を保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!File.ReadAllText(propertiesPath).Contains("difficulty=3") || !File.ReadAllText(propertiesPath).Contains("gamemode=adventure")) throw new Exception("Edited legacy property was not converted safely");
            Console.WriteLine("PASS server.properties GUI edit/save/reopen, port sync, secret field and comment preservation");
            navigate.Invoke(window, ["overview"]); Layout();
            var windowStore = (HarborStore)typeof(MainWindow).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var removed = windowStore.Add("external-folder-deleted");
            typeof(MainWindow).GetMethod("RefreshServers", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [null]);
            Directory.Delete(windowStore.ServerDir(removed));
            for (var i = 0; i < 10; i++) typeof(MainWindow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            if (windowStore.Profiles.Any(x => x.Id == removed.Id) || Descendants(content).OfType<ListBox>().First().Items.Count != 1) throw new Exception("Deleted server folder remains in the sidebar");
            Layout();
            Console.WriteLine($"INFO UI process working set: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576d:F1} MB (test host)");
            if (args.Length > 0)
            {
                SaveImage(content, args[0]);
            }
            window.Close();
            void PumpUntil(Func<bool> complete)
            {
                var frame = new System.Windows.Threading.DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(5);
                var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                poll.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
                poll.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); poll.Stop();
                if (!complete()) throw new Exception("Startup test timed out");
            }
            var gate = new TaskCompletionSource<HarborStore>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = new StartupWindow { ShowActivated = false }; bool rendered = false; Task? loadTask = null;
            loading.ContentRendered += (_, _) => { if (rendered) return; rendered = true; loadTask = loading.LoadAsync(() => gate.Task); };
            loading.Show(); PumpUntil(() => rendered && loadTask != null);
            if (!loading.IsVisible || loadTask!.IsCompleted) throw new Exception("Loading window must render before storage completes");
            bool responsive = false; loading.Dispatcher.BeginInvoke(() => responsive = true); PumpUntil(() => responsive);
            gate.SetResult(store); PumpUntil(() => loadTask.IsCompleted);
            if (loading.IsVisible || app.MainWindow is not MainWindow ready || !ready.IsVisible) throw new Exception("Loading handoff failed");
            app.MainWindow.Close();
            var cancelGate = new TaskCompletionSource<HarborStore>(); var cancelled = new StartupWindow { ShowActivated = false };
            cancelled.Show(); var cancelledTask = cancelled.LoadAsync(() => cancelGate.Task); cancelled.Close(); cancelGate.SetResult(store); PumpUntil(() => cancelledTask.IsCompleted);
            if (app.Windows.OfType<MainWindow>().Any(w => w.IsVisible)) throw new Exception("Closing loader reopened main window");
            var failure = new StartupWindow { ShowActivated = false }; failure.Show();
            var failedTask = failure.LoadAsync(() => Task.FromException<HarborStore>(new IOException("読み込みテスト"))); PumpUntil(() => failedTask.IsCompleted);
            if (!failure.IsVisible || !failure.Title.Contains("起動できませんでした")) throw new Exception("Startup failure feedback missing");
            failure.Close();
            Console.WriteLine("PASS startup render-before-load, responsive dispatcher, handoff, close cancellation and failure feedback");
            Console.WriteLine("RESULT UI smoke passed; no Minecraft processes launched"); return 0;
            static void awaitVersions(Task task) => task.GetAwaiter().GetResult();
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (Directory.Exists(root))
            {
                if (Environment.GetEnvironmentVariable("CI") == "true") Directory.Delete(root, true);
                else Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(root,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
        }
    }
    private static void SaveImage(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap(1240, 840, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void SaveMenuImage(ContextMenu menu, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(menu.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(menu.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void AssertColor(string path, string sample, string token, string role)
    {
        var syntax = ConfigurationSyntax.ForPath(path) ?? throw new Exception("Missing syntax for " + path);
        var document = new TextDocument(sample);
        using var highlighter = new DocumentHighlighter(document, syntax);
        var offset = sample.IndexOf(token, StringComparison.Ordinal);
        if (offset < 0) throw new Exception("Missing sample token " + token);
        var line = document.GetLineByOffset(offset).LineNumber;
        var sections = highlighter.HighlightLine(line).Sections;
        if (!sections.Any(s => s.Offset <= offset && offset < s.Offset + s.Length && s.Color.Name == role))
            throw new Exception($"Wrong {path} color for {token}: expected {role}; got " + string.Join(", ", sections.Select(s => $"{s.Offset}:{s.Length}:{s.Color.Name}")));
    }
}
