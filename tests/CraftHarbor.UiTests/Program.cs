using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftHarbor.Core;
using CraftHarbor.Desktop;

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
                foreach (var key in new[] { "overview", "console", "backups", "launch", "resources", "advanced", "install", "import", "properties", "manage", "mods", "modsearch", "presets", "modpacks", "automodpack", "modsettings", "files", "java", "system", "network", "help", "updates", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    Console.WriteLine($"PASS UI navigation/layout {key} round {pass + 1}");
                }
            navigate.Invoke(window, ["launch"]); Layout();
            var engine = Descendants(content).OfType<ComboBox>().Single(x => x.Name == "ServerEngine");
            var fabric = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "FabricSettings");
            var versionButton = Descendants(content).OfType<Button>().Single(x => x.Name == "ServerVersions");
            var saveButton = Descendants(content).OfType<Button>().Single(x => x.Content?.ToString() == "設定を保存");
            foreach (var kind in new[] { "vanilla", "paper", "folia", "forge", "neoforge", "quilt", "custom", "fabric" })
            {
                engine.SelectedItem = kind; Layout();
                if ((fabric.Visibility == Visibility.Visible) != (kind == "fabric")) throw new Exception("Incorrect Fabric fields for " + kind);
                if (versionButton.IsEnabled != (kind is "vanilla" or "paper" or "folia" or "fabric")) throw new Exception("Incorrect version selector for " + kind);
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
                foreach (var key in new[] { "overview", "console", "backups", "launch", "resources", "advanced", "install", "import", "properties", "manage", "mods", "modsearch", "presets", "modpacks", "automodpack", "modsettings", "files", "java", "system", "network", "help", "updates", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    var expected = ((SolidColorBrush)app.Resources["Input"]).Color;
                    foreach (var box in Descendants(content).OfType<ComboBox>())
                    {
                        if (((SolidColorBrush)box.Background).Color != expected) throw new Exception("ComboBox has incorrect theme");
                        if (box.Template.FindName("PART_Popup", box) is not System.Windows.Controls.Primitives.Popup popup || popup.Child is not Border popupBorder || ((SolidColorBrush)popupBorder.Background).Color != expected) throw new Exception("Dropdown has incorrect theme");
                    }
                    if (((SolidColorBrush)((Grid)content).Background).Color != ((SolidColorBrush)app.Resources["Background"]).Color) throw new Exception("Local background did not update");
                    if (args.Length > 0 && key is "launch" or "appearance") SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, $"{appearance.ToLowerInvariant()}-{key}.png"));
                }
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
            var configs = Descendants(content).OfType<ComboBox>().Single();
            if (!configs.Items.Cast<string>().Contains(Path.Combine("automodpack", "automodpack-server.json")) || configs.Items.Cast<string>().Any(x => x.EndsWith("automodpack-client.json"))) throw new Exception("AutoModpack configuration scope incorrect");
            configs.SelectedItem = Path.Combine("automodpack", "automodpack-server.json");
            Descendants(content).OfType<TextBox>().Single().Text = "{\"modpackName\":\"日本語同期テスト\"}";
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "ファイルを保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!File.ReadAllText(Path.Combine(autoDir, "automodpack-server.json")).Contains("日本語同期テスト") || !SafeFiles.Files(Path.Combine(root, "file-history")).Any()) throw new Exception("AutoModpack save/history failed");
            var extraConfigs = new[] { "config/ftb.snbt", "config/mod.json5", "defaultconfigs/create.toml", "Adventure/serverconfig/mod.toml", "kubejs/server_scripts/test.js", "scripts/test.zs" };
            foreach (var relative in extraConfigs) { var file = Path.Combine(serverDir, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "original"); }
            navigate.Invoke(window, ["files"]); Layout(); configs = Descendants(content).OfType<ComboBox>().Single();
            foreach (var relative in extraConfigs)
            {
                var item = relative.Replace('/', Path.DirectorySeparatorChar); if (!configs.Items.Contains(item)) throw new Exception("Missing config " + relative);
                configs.SelectedItem = item; Descendants(content).OfType<TextBox>().Single().Text = "edited 日本語";
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
            var links = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "NavigationLinks");
            if (links.Children.OfType<Button>().Count() < 20) throw new Exception("Page navigation is incomplete");
            if (Descendants(content).OfType<WrapPanel>().Any(x => x.Name is "NavigationGroups" or "SectionCategories")) throw new Exception("Old stacked navigation remains");
            links.Children.OfType<Button>().Single(b => b.Name == "Navresources").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
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
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void SaveImage(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap(1240, 840, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
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
}
