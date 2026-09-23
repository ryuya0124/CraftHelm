using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CraftHarbor.Core;
using Microsoft.Win32;
using MessageBox = CraftHarbor.Desktop.HarborDialog;

namespace CraftHarbor.Desktop;

public sealed class MainWindow : Window
{
    private readonly HarborStore store;
    private readonly UpdateManager updater;
    private TextBlock? updateStatus;
    private bool restartForUpdate;
    private readonly Downloads downloads = new();
    private readonly Dictionary<string, ServerRuntime> runtimes = [];
    private readonly ListBox servers = new();
    private readonly StackPanel page = new();
    private readonly ScrollViewer pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel navigationLinks = new() { Name = "NavigationLinks" };
    private Button? appSettingsButton;
    private StackPanel? detailContent;
    private StackPanel ContentPanel => detailContent ?? page;
    private static readonly (string Group, string Label, string Key)[] Routes = [
        ("運用", "概要", "overview"), ("運用", "コンソール", "console"), ("運用", "バックアップ", "backups"),
        ("サーバー設定", "ホーム", "server-settings"),
        ("サーバー", "種類とバージョン", "launch"), ("サーバー", "Java・メモリ・ポート", "resources"), ("サーバー", "起動引数", "advanced"),
        ("サーバー", "本体の導入", "install"), ("サーバー", "フォルダの取り込み", "import"), ("サーバー", "ゲーム・接続設定", "properties"), ("サーバー", "サーバーを削除", "manage"),
        ("MOD・プラグイン", "インストール済み", "mods"), ("MOD・プラグイン", "MODを検索", "modsearch"), ("MOD・プラグイン", "構成プリセット", "presets"),
        ("MOD・プラグイン", "MODパック", "modpacks"), ("MOD・プラグイン", "AutoModpack", "automodpack"), ("MOD・プラグイン", "MODの設定", "modsettings"), ("MOD・プラグイン", "テキスト編集", "files"),
        ("アプリ設定", "ホーム", "settings"), ("アプリ設定", "表示", "appearance"), ("アプリ設定", "更新", "updates"), ("アプリ設定", "Javaの管理", "java"), ("アプリ設定", "PC情報", "system"), ("アプリ設定", "接続診断", "network"), ("アプリ設定", "ガイド", "help")
    ];
    private readonly TextBlock title = new() { FontSize = 28, FontWeight = FontWeights.Bold };
    private readonly TextBlock subtitle = new() { Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 8, 0, 20) };
    private readonly TextBlock status = new() { Text = "準備完了  •  データはこのPCに保存", Foreground = Brush("#61DBC4"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock metrics = new() { Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TextBox? console;
    private bool busy;
    private CancellationTokenSource? operation;
    private string currentPage = "overview";
    private Func<bool>? mayLeave;
    private bool restoringSelection;
    private int reconcileTicks;
    private ServerProfile? Selected => servers.SelectedItem as ServerProfile;
    private ServerRuntime Runtime(ServerProfile p)
    {
        if (!runtimes.TryGetValue(p.Id, out var runtime)) runtimes[p.Id] = runtime = new ServerRuntime(); return runtime;
    }
    private static SolidColorBrush Brush(string color) => Theme.Brush(color);
    public MainWindow(string root) : this(new HarborStore(root)) { }
    public MainWindow(HarborStore loadedStore)
    {
        store = loadedStore; updater = new UpdateManager(store.Root); updater.Changed += () => { if (updateStatus != null) updateStatus.Text = updater.Status; }; Theme.Load(store.Root); Style = (Style)FindResource(typeof(Window)); Theme.Attach(this);
        Title = "CraftHelm — Minecraft Server Manager"; Width = 1240; Height = 840; MinWidth = 980; MinHeight = 700; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Background = Brush("#0D141F") }; layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Content = layout;
        var sidebar = new DockPanel { Background = Brush("#111B29"), Margin = new Thickness(0) }; layout.Children.Add(sidebar);
        var brand = new StackPanel { Margin = new Thickness(22, 28, 18, 20) };
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        brandRow.Children.Add(new Image { Source = Theme.Icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) });
        brandRow.Children.Add(new TextBlock { Text = "CraftHelm", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brush("#61DBC4"), VerticalAlignment = VerticalAlignment.Center }); brand.Children.Add(brandRow);
        brand.Children.Add(new TextBlock { Text = "Minecraft Server Manager", FontSize = 10, Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 8, 0, 24) });
        brand.Children.Add(Btn("＋ サーバーを追加", AddServer, true)); brand.Children.Add(new TextBlock { Text = "サーバー", Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 12, 0, 8) });
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Margin = new Thickness(20) };
        appSettingsButton = Btn("アプリ設定", () => Navigate("settings")); footer.Children.Add(appSettingsButton);
        footer.Children.Add(new TextBlock { Text = "v" + typeof(App).Assembly.GetName().Version!.ToString(3) + "  •  Windows版", FontSize = 11, Foreground = Brush("#91A3B8") });
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        servers.Margin = new Thickness(12, 0, 12, 8); servers.Height = 150; DockPanel.SetDock(servers, Dock.Top); sidebar.Children.Add(servers);
        var serverMenu = new ContextMenu();
        foreach (var (label, destination) in new[] { ("サーバー設定", "server-settings"), ("サーバーを削除…", "manage") })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { if (Selected != null) Navigate(destination); };
            serverMenu.Items.Add(item);
        }
        servers.ContextMenu = serverMenu;
        servers.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(servers, e.OriginalSource as DependencyObject) is ListBoxItem item)
                servers.SelectedItem = item.DataContext;
            else e.Handled = true;
        };
        sidebar.Children.Add(new ScrollViewer { Content = navigationLinks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        servers.SelectionChanged += (_, e) =>
        {
            if (busy || restoringSelection) return;
            if (mayLeave != null && !mayLeave())
            {
                restoringSelection = true;
                servers.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
                restoringSelection = false; return;
            }
            mayLeave = null; Navigate(currentPage);
        };
        var main = new DockPanel { Margin = new Thickness(30, 26, 30, 16) }; Grid.SetColumn(main, 1); layout.Children.Add(main);
        var header = new StackPanel(); header.Children.Add(title); header.Children.Add(subtitle); DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var cancel = Btn("処理をキャンセル", () => operation?.Cancel()); DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel); bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); main.Children.Add(bottom);
        pageScroll.Content = page; main.Children.Add(pageScroll);
        store.ReconcileMissingServerFolders(); RefreshServers(); Navigate("overview");
        timer.Tick += (_, _) => Tick(); timer.Start(); Closing += OnClosing;
    }
    private void RefreshServers(ServerProfile? select = null)
    {
        var selection = select ?? Selected;
        restoringSelection = true;
        servers.ItemsSource = null; servers.ItemsSource = store.Profiles;
        servers.SelectedItem = selection != null && store.Profiles.Contains(selection) ? selection : store.Profiles.FirstOrDefault();
        restoringSelection = false;
    }
    private Button Btn(string text, Action action, bool accent = false, bool allowWhileBusy = false)
    {
        var button = new Button { Content = text }; if (accent) { button.Background = Brush("#61DBC4"); button.Foreground = Brush("#102823"); }
        button.Click += (_, _) => { if (busy && text != "処理をキャンセル" && !allowWhileBusy) { status.Text = "処理中です。完了を待つかキャンセルしてください。"; return; } try { action(); } catch (Exception ex) { Error(ex); } }; return button;
    }
    private Button AsyncBtn(string text, Func<CancellationToken, Task> action, bool accent = false) => Btn(text, () => _ = Run(action), accent);
    private Button CommandButton(string command, ServerRuntime runtime)
    {
        var button = AsyncBtn(JapaneseDisplay.Label(command), async _ => await runtime.SendAsync(command));
        button.Tag = command; button.ToolTip = "送信するコマンド: " + command; return button;
    }
    private void RefreshNavigation(string key)
    {
        navigationLinks.Children.Clear();
        var section = Routes.First(r => r.Key == key).Group;
        if (appSettingsButton != null)
        {
            appSettingsButton.Background = Brush(section == "アプリ設定" ? "Accent" : "Button");
            appSettingsButton.Foreground = Brush(section == "アプリ設定" ? "AccentInk" : "Ink");
        }
        foreach (var group in new[] { "運用", "サーバー設定" })
        {
            if (group == "運用") navigationLinks.Children.Add(new TextBlock { Text = group, Foreground = Brush("#91A3B8"), FontSize = 12, Margin = new Thickness(20, 14, 0, 5) });
            foreach (var route in Routes.Where(r => r.Group == group))
            {
                var active = route.Key == key || (route.Key == "server-settings" && section is ("サーバー" or "MOD・プラグイン"));
                var button = Btn(group == "サーバー設定" ? group : route.Label, () => Navigate(route.Key), active);
                button.Name = "Nav" + route.Key.Replace("-", "");
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(12, group == "サーバー設定" ? 16 : 2, 12, 2);
                navigationLinks.Children.Add(button);
            }
        }
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return; busy = true; servers.IsEnabled = false; operation = new CancellationTokenSource(); status.Text = "処理中…";
        try { await action(operation.Token); status.Text = "完了"; }
        catch (OperationCanceledException) { status.Text = "キャンセルしました"; }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; servers.IsEnabled = true; operation.Dispose(); operation = null; }
    }
    private void Error(Exception ex) { status.Text = "エラー: " + ex.Message; MessageBox.Show(this, ex.Message, "CraftHelm", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static TextBlock Text(string value, int size = 14) => new() { Text = value, FontSize = size, Margin = new Thickness(0, 0, 0, 12) };
    private TextBox Field(Panel parent, string label, string value, bool multiline = false)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Brush("#AAB9CA") });
        var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 88 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden }; System.Windows.Automation.AutomationProperties.SetName(box, label); parent.Children.Add(box); return box;
    }
    private static StackPanel Card(Panel parent, string heading)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(heading, 18));
        parent.Children.Add(new Border { Background = Brush("#192330"), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Margin = new Thickness(0, 12, 0, 0), Child = panel }); return panel;
    }
    private IProgress<string> Progress() => new Progress<string>(s => status.Text = s);
    private void Stopped(ServerProfile p) { if (Runtime(p).Running || Runtime(p).Busy) throw new InvalidOperationException("この操作はサーバー停止中に行ってください。"); }
    private bool Confirm(string message) => MessageBox.Show(this, message, "CraftHelm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void Navigate(string key)
    {
        if (busy || (mayLeave != null && !mayLeave())) return; mayLeave = null; currentPage = key; page.Children.Clear(); detailContent = null; console = null; updateStatus = null;
        RefreshNavigation(key);
        var route = Routes.First(r => r.Key == key);
        var appSettings = route.Group == "アプリ設定";
        var serverSettings = route.Group is "サーバー設定" or "サーバー" or "MOD・プラグイン";
        title.Text = appSettings ? "アプリ設定" : Selected?.Name ?? "サーバーを追加";
        subtitle.Text = (appSettings ? "アプリ設定" : serverSettings ? "サーバー設定" : route.Group) + " › " + route.Label + (!appSettings && Selected is { } p ? $"  •  {JapaneseDisplay.Label(p.Engine)} / Minecraft {p.Version}" : "");
        if (appSettings) BuildDetailNavigation(key, true);
        else if (serverSettings && Selected != null) BuildDetailNavigation(key, false);
        if (key == "settings") { SettingsHomePage(); return; }
        if (key == "updates") { UpdatesPage(); return; } if (key == "java") { JavaPage(); return; } if (key == "system") { SystemPage(); return; } if (key == "network") { NetworkPage(); return; } if (key == "help") { HelpPage(); return; } if (key == "appearance") { AppearancePage(); return; }
        if (Selected == null) { Welcome(); return; }
        switch (key) { case "server-settings": ServerSettingsHomePage(); break; case "console": ConsolePage(); break; case "launch": LaunchPage(); break; case "resources": ResourcesPage(); break; case "advanced": AdvancedPage(); break; case "install": InstallPage(); break; case "import": ImportPage(); break; case "manage": ManagePage(); break; case "mods": ModsPage(); break; case "modsearch": ModSearchPage(); break; case "presets": PresetsPage(); break; case "modpacks": ModpacksPage(); break; case "automodpack": AutoModpackPage(); break; case "modsettings": ModSettingsPage(); break; case "properties": PropertiesPage(); break; case "files": FilesPage(); break; case "backups": BackupsPage(); break; default: Overview(); break; }
    }
    private void BuildDetailNavigation(string key, bool appSettings)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(192) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); page.Children.Add(grid);
        var links = new StackPanel { Name = appSettings ? "SettingsNavigation" : "ServerSettingsNavigation", Margin = new Thickness(0, 0, 8, 0) };
        var linksScroll = new ScrollViewer { Content = links, Margin = new Thickness(0, 0, 12, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        linksScroll.SetBinding(FrameworkElement.MaxHeightProperty, new Binding(nameof(ActualHeight)) { Source = pageScroll }); grid.Children.Add(linksScroll);
        detailContent = new StackPanel { Name = "DetailContent" }; Grid.SetColumn(detailContent, 1); grid.Children.Add(detailContent);
        var groups = appSettings ? new[] { "アプリ設定" } : new[] { "サーバー設定", "サーバー", "MOD・プラグイン" };
        foreach (var group in groups)
        {
            links.Children.Add(new TextBlock { Text = group, Foreground = Brush("#91A3B8"), FontSize = 12, Margin = new Thickness(4, 12, 0, 5) });
            foreach (var route in Routes.Where(r => r.Group == group))
            {
                var button = Btn(route.Label, () => Navigate(route.Key), route.Key == key);
                button.Name = (appSettings ? "SettingNav" : "ServerSettingNav") + route.Key.Replace("-", "");
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 2, 0, 2); links.Children.Add(button);
            }
        }
    }
    private void SettingsHomePage()
    {
        var card = Card(ContentPanel, "CraftHelmの設定");
        card.Children.Add(Text("表示、更新、Java、PC情報、接続診断を左側から選べます。これらの設定はサーバーを選ばなくても開けます。"));
        card.Children.Add(Text("現在の表示: " + (Theme.Appearance == "Dark" ? "ダーク" : "ライト") + "  •  バージョン: " + typeof(App).Assembly.GetName().Version!.ToString(3), 12));
    }
    private void ServerSettingsHomePage()
    {
        var card = Card(ContentPanel, "サーバーの設定");
        card.Children.Add(Text("種類とバージョン、Java・メモリ・ポート、ゲーム設定を左側から選べます。MOD・プラグインの導入、構成プリセット、設定ファイルもここで管理します。"));
        card.Children.Add(Text("対象: " + Selected!.Name + "  •  " + store.ServerDir(Selected), 12));
    }
    private void Welcome()
    {
        var card = Card(ContentPanel, "最初のワールドを迎えましょう");
        card.Children.Add(Text("1  サーバーを追加して種類とバージョンを選択\n2  Javaを選び、サーバー本体をダウンロード\n3  EULAを確認して起動")); card.Children.Add(Btn("＋ 最初のサーバーを追加", AddServer, true));
        var features = Card(ContentPanel, "軽く動いて、しっかり管理");
        features.Children.Add(Text("リアルタイムコンソール  •  複数サーバー  •  Java 8 / 11 / 17 / 21 / 25\nMOD・プラグインの切替  •  構成プリセット  •  Modrinth検索 / mrpack\n設定ファイル編集  •  ZIPバックアップ / 復元  •  TCP疎通確認"));
        features.Children.Add(Text("すでに別のアプリで動いているサーバーは操作しません。既存サーバーの取り込みは、元サーバーを停止できる時にコピーで行います。"));
    }
    private void AddServer()
    {
        var name = Ask("サーバー名", "マイサーバー"); if (string.IsNullOrWhiteSpace(name)) return;
        var p = store.Add(name); RefreshServers(p); Navigate("launch");
    }
    private void ManagePage()
    {
        var p = Selected!; var directory = store.ServerDir(p);
        var card = Card(ContentPanel, "選択サーバーを削除");
        card.Children.Add(Text($"対象: {p.Name}\n保存先: {directory}"));
        card.Children.Add(Text("削除は選択中のサーバーだけが対象です。稼働中は削除できません。バックアップとログは残します。", 12));
        card.Children.Add(Btn("一覧からのみ削除", () =>
        {
            Stopped(p); if (!Confirm($"{p.Name} を一覧から削除しますか？ サーバーフォルダは残ります。")) return;
            store.Remove(p); RefreshServers(); Navigate("overview"); status.Text = "一覧から削除しました";
        }));
        card.Children.Add(AsyncBtn("サーバーフォルダもゴミ箱へ移動", async ct =>
        {
            Stopped(p); if (!Confirm($"{p.Name} の登録とサーバーフォルダを削除しますか？\n{directory}\nフォルダはWindowsのゴミ箱へ移動します。バックアップは残ります。")) return;
            if (Directory.Exists(directory)) await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), ct);
            store.Remove(p); RefreshServers(); _ = Dispatcher.BeginInvoke(() => Navigate("overview"), DispatcherPriority.Background);
        }));
    }
    private string? Ask(string label, string initial)
    {
        var dialog = new Window { Owner = this, Title = label, Width = 520, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        Theme.Attach(dialog);
        var body = new StackPanel { Margin = new Thickness(24) }; var box = Field(body, label, initial);
        var ok = new Button { Content = "決定", IsDefault = true }; ok.Click += (_, _) => dialog.DialogResult = true; body.Children.Add(ok); dialog.Content = body; box.Focus(); return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    private void Overview()
    {
        var p = Selected!; var runtime = Runtime(p);
        var card = Card(ContentPanel, "サーバーコントロール");
        var row = new WrapPanel(); row.Children.Add(Btn("▶ 起動", () => Start(p), true));
        row.Children.Add(AsyncBtn("■ 安全に停止", async _ => await runtime.StopAsync()));
        row.Children.Add(AsyncBtn("↻ 再起動", async _ => { await runtime.StopAsync(); Start(p); }));
        row.Children.Add(Btn("フォルダを開く", () => Open(store.ServerDir(p)))); card.Children.Add(row); if (metrics.Parent is Panel previous) previous.Children.Remove(metrics); card.Children.Add(metrics);
        var info = Card(ContentPanel, "このサーバーの構成"); info.Children.Add(Text($"種類: {JapaneseDisplay.Label(p.Engine)}   Minecraft: {p.Version}\nメモリ: {p.MinMemoryMb} – {p.MaxMemoryMb} MB\nJava: {p.JavaPath}\n起動: {(p.LaunchArgs.Length == 0 ? p.Jar : string.Join(" ", p.LaunchArgs))}\n保存先: {store.ServerDir(p)}"));
        info.Children.Add(Text("起動前にJavaとMODの対応バージョンを確認してください。Java要件はサーバーのダウンロード後に表示します。"));
        var next = Card(ContentPanel, "次の操作");
        next.Children.Add(Text("左側からコンソール、バックアップ、サーバー設定を選べます。MOD・プラグインはサーバー設定の中で管理できます。"));
    }
    private void Start(ServerProfile p)
    {
        Runtime(p).Start(p, store.ServerDir(p), Path.Combine(store.Root, "logs", p.Id)); status.Text = "サーバーを起動しました。コンソールで準備状況を確認できます。";
    }
    private void ConsolePage()
    {
        var p = Selected!; var runtime = Runtime(p);
        var row = new WrapPanel(); row.Children.Add(Btn("▶ 起動", () => Start(p), true)); row.Children.Add(AsyncBtn("■ 安全に停止", async _ => await runtime.StopAsync()));
        row.Children.Add(Btn("強制終了", () => { if (Confirm("このアプリから起動した選択サーバーを強制終了します。ワールドが破損する場合があります。実行しますか？")) runtime.Kill(); }));
        row.Children.Add(Btn("ログを開く", () => { var dir = Path.Combine(store.Root, "logs", p.Id); Directory.CreateDirectory(dir); Open(dir); })); page.Children.Add(row);
        console = new TextBox { Text = string.Join(Environment.NewLine, runtime.History), IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Height = 350, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#080E17") }; page.Children.Add(console);
        var command = Field(page, "サーバーコマンド（先頭の / は不要）", "");
        async Task Send(CancellationToken _) { await runtime.SendAsync(command.Text); command.Clear(); }
        command.KeyDown += (_, e) => { if (e.Key == Key.Enter && !busy) { e.Handled = true; _ = Run(Send); } };
        var controls = new WrapPanel(); controls.Children.Add(AsyncBtn("送信 ↵", Send, true));
        foreach (var cmd in new[] { "list", "save-all", "whitelist list" }) controls.Children.Add(CommandButton(cmd, runtime));
        if (File.Exists(Path.Combine(store.ServerDir(p), "automodpack", "automodpack-server.json")))
            foreach (var cmd in new[] { "automodpack", "automodpack host", "automodpack generate", "automodpack config reload" }) controls.Children.Add(CommandButton(cmd, runtime));
        page.Children.Add(controls); page.Children.Add(Text("画面は最大2,000行。完全な出力は logs に保存します。停止操作はCraftHelmが起動したプロセスだけが対象です。", 12));
    }
    private void Tick()
    {
        if (!busy && ++reconcileTicks >= 10)
        {
            reconcileTicks = 0;
            try
            {
                if (store.ReconcileMissingServerFolders(p => runtimes.TryGetValue(p.Id, out var r) && (r.Running || r.Busy)) > 0)
                {
                    RefreshServers(); Navigate(currentPage); status.Text = "フォルダがないサーバーを一覧から除きました";
                }
            }
            catch (Exception ex) { status.Text = "一覧の更新に失敗: " + ex.Message; }
        }
        foreach (var (id, runtime) in runtimes)
        {
            var fresh = runtime.Drain();
            if (console != null && Selected?.Id == id && fresh.Count > 0)
            {
                var follow = console.VerticalOffset >= console.ExtentHeight - console.ViewportHeight - 30;
                console.AppendText(string.Join(Environment.NewLine, fresh) + Environment.NewLine);
                if (console.LineCount > 2200 || console.Text.Length > 1000000) console.Text = string.Join(Environment.NewLine, runtime.History);
                if (follow) console.ScrollToEnd();
            }
        }
        if (Selected is { } p)
        {
            var r = Runtime(p); metrics.Text = $"{(r.Running ? r.Ready ? "● 稼働中" : "● 起動中 / ログを確認" : "○ 停止中")}   プロセス番号 {r.Pid?.ToString() ?? "—"}   メモリ {r.WorkingSet / 1048576d:F0} MB";
        }
    }
    private void LaunchPage()
    {
        var p = Selected!; var card = Card(ContentPanel, "起動プロファイル");
        var name = Field(card, "名前", p.Name);
        card.Children.Add(Text("サーバーの種類（Forge / NeoForgeは導入済みサーバーを取り込みます）", 12));
        var engine = new ComboBox { Name = "ServerEngine", ItemsSource = new[] { "vanilla", "paper", "fabric", "folia", "forge", "neoforge", "quilt", "custom" }, SelectedItem = p.Engine }; JapaneseDisplay.Apply(engine); card.Children.Add(engine);
        var version = Field(card, "Minecraft バージョン", p.Version);
        var fabricFields = new StackPanel { Name = "FabricSettings" }; card.Children.Add(fabricFields);
        var loaderVersion = Field(fabricFields, "Fabricローダーバージョン（空欄で最新安定版・パック指定版も入力可能）", p.Engine == "fabric" ? p.LoaderVersion : "");
        var versionHint = Text("", 12);
        var versionButton = AsyncBtn("対応バージョン一覧を取得", async ct =>
        {
            var selectedEngine = engine.SelectedItem?.ToString() ?? "custom";
            var versions = await downloads.ServerVersions(selectedEngine, ct);
            if (engine.SelectedItem?.ToString() != selectedEngine) { MessageBox.Show(this, "取得中に種類が変わりました。新しい種類で一覧を取得し直してください。"); return; }
            if (versions.Length == 0) throw new IOException("この種類の対応リリースが見つかりません。");
            var chosen = Choose($"{selectedEngine} — Minecraft バージョン", versions); if (chosen != null) version.Text = chosen;
        });
        versionButton.Name = "ServerVersions"; card.Children.Add(versionButton); card.Children.Add(versionHint);
        void UpdateEngineFields()
        {
            var selectedEngine = engine.SelectedItem?.ToString() ?? "custom";
            fabricFields.Visibility = selectedEngine == "fabric" ? Visibility.Visible : Visibility.Collapsed;
            versionButton.IsEnabled = selectedEngine is "vanilla" or "fabric" or "paper" or "folia";
            versionHint.Text = selectedEngine switch
            {
                "fabric" => "Fabric対応のMinecraftリリースを表示します。",
                "paper" or "folia" => "配布元に存在するMinecraftバージョンを表示します。安定ビルドの有無は導入時に確認します。",
                "vanilla" => "VanillaのMinecraftリリースを表示します。Fabricローダーの設定は使用しません。",
                _ => "手動取り込み用です。導入済みサーバーのMinecraftバージョンを入力してください。"
            };
        }
        engine.SelectionChanged += (_, _) => UpdateEngineFields(); UpdateEngineFields();
        card.Children.Add(Btn("設定を保存", () =>
        {
            Stopped(p);
            var newName = name.Text.Trim(); if (string.IsNullOrWhiteSpace(newName)) throw new IOException("名前を入力してください。");
            p.Name = newName; p.Engine = engine.SelectedItem?.ToString() ?? "custom";
            p.Version = version.Text.Trim(); p.LoaderVersion = p.Engine == "fabric" ? loaderVersion.Text.Trim() : "";
            store.Save(); RefreshServers(p); status.Text = "種類とバージョンを保存しました";
        }, true));
    }
    private void ResourcesPage()
    {
        var p = Selected!;
        var card = Card(ContentPanel, "Java・メモリ・ポート");
        var java = Field(card, "Java実行ファイルの場所", p.JavaPath);
        card.Children.Add(Btn("java.exe を選択", () => { var file = Pick("Java|java.exe"); if (file != null) java.Text = file; }));
        var min = Field(card, "最小メモリ MB", p.MinMemoryMb.ToString()); var max = Field(card, "最大メモリ MB", p.MaxMemoryMb.ToString()); var port = Field(card, "サーバーポート", p.Port.ToString());
        card.Children.Add(Btn("リソース設定を保存", () =>
        {
            Stopped(p);
            var draft = new ServerProfile { Name = p.Name, MinMemoryMb = int.Parse(min.Text), MaxMemoryMb = int.Parse(max.Text), Port = int.Parse(port.Text), JavaPath = java.Text.Trim() };
            draft.Validate(); p.MinMemoryMb = draft.MinMemoryMb; p.MaxMemoryMb = draft.MaxMemoryMb; p.Port = draft.Port; p.JavaPath = draft.JavaPath;
            store.Save(); status.Text = "Java・メモリ・ポートを保存しました";
        }, true));
        card.Children.Add(Text("25565が使用中なら別のポートを指定してください。ゲーム・接続設定のserver-portも確認してください。", 12));
    }
    private void AdvancedPage()
    {
        var p = Selected!; var card = Card(ContentPanel, "詳細な起動引数");
        var jar = Field(card, "サーバー本体ファイル（サーバーフォルダ内の場所）", p.Jar);
        var jvm = Field(card, "Javaへの追加オプション（1行に1つ・引用符不要）", string.Join(Environment.NewLine, p.JvmArgs), true);
        var args = Field(card, "サーバーへの起動オプション（1行に1つ・通常は空欄）", string.Join(Environment.NewLine, p.LaunchArgs), true);
        card.Children.Add(Text("Forge / NeoForge例: @libraries/net/neoforged/neoforge/…/win_args.txt と nogui を別々の行に指定。シェル / BATは実行しません。", 12));
        card.Children.Add(Btn("起動引数を保存", () =>
        {
            Stopped(p); var newJar = jar.Text.Trim(); _ = SafeFiles.Inside(store.ServerDir(p), newJar);
            p.Jar = newJar; p.JvmArgs = Lines(jvm.Text); p.LaunchArgs = Lines(args.Text); store.Save(); status.Text = "起動引数を保存しました";
        }, true));
    }
    private void InstallPage()
    {
        var p = Selected!; var card = Card(ContentPanel, "サーバー本体の導入");
        card.Children.Add(Text($"種類: {JapaneseDisplay.Label(p.Engine)}  Minecraft: {p.Version}  Java: {p.JavaPath}"));
        var eula = new CheckBox { Content = "Minecraftの利用規約を読み、このサーバーでの利用に同意する", IsChecked = p.EulaAccepted }; card.Children.Add(eula); card.Children.Add(Btn("Minecraftの利用規約を開く", () => Open("https://www.minecraft.net/eula")));
        card.Children.Add(AsyncBtn("サーバー本体を導入", async ct =>
        {
            Stopped(p); p.EulaAccepted = eula.IsChecked == true; store.Save();
            if (File.Exists(Path.Combine(store.ServerDir(p), "server.jar")))
            {
                if (!Confirm("server.jarを更新します。先にサーバー全体のバックアップを作成します。続行しますか？")) return;
                await Task.Run(() => SafeFiles.Snapshot(store.ServerDir(p), store.BackupDir(p), cancellationToken: ct), ct);
            }
            var required = await downloads.InstallServer(p, store.ServerDir(p), Progress(), ct); store.Save();
            MessageBox.Show(this, $"導入完了。Minecraftのメタデータ上のJava要件: {required}\nPaper等は独自の要件も確認してください。Java画面で使用Javaを選択できます。", "サーバーを導入しました");
        }, true));
    }
    private void ImportPage()
    {
        var p = Selected!; var imports = Card(ContentPanel, "既存サーバーをコピーして取り込む");
        imports.Children.Add(Text("元フォルダは変更しません。稼働中のワールドはコピーしないでください。取り込み先は空の新規サーバーのみです。"));
        imports.Children.Add(AsyncBtn("停止済みフォルダを選んでコピー", async ct =>
        {
            Stopped(p); var dialog = new OpenFolderDialog { Title = "停止済みサーバーフォルダ" }; if (dialog.ShowDialog(this) != true) return;
            var source = Path.GetFullPath(dialog.FolderName); var target = store.ServerDir(p);
            if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("取り込み元と先が重なっています。");
            if (SafeFiles.Files(target).Any()) throw new IOException("空の新規サーバーを使用してください。");
            if (!Confirm("元サーバーが停止していることを確認しましたか？稼働中の場合は「いいえ」を選んでください。")) return;
            var stage = target + ".import-" + Guid.NewGuid().ToString("N");
            try { await Task.Run(() => SafeFiles.CopyTree(source, stage), ct); ct.ThrowIfCancellationRequested(); SafeFiles.PromoteIntoEmptyDirectory(stage, target); }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            MessageBox.Show(this, "コピーしました。JARパスまたはカスタム起動引数とJavaを設定してください。");
        }));
    }
    private static string[] Lines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private string? Pick(string filter) { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    private string? Choose(string heading, IEnumerable<string> values)
    {
        var dialog = new Window { Owner = this, Title = heading, Width = 620, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Theme.Attach(dialog);
        var body = new DockPanel { Margin = new Thickness(20) }; var list = new ListBox { ItemsSource = values.ToArray() }; var button = new Button { Content = "選択" }; button.Click += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; }; DockPanel.SetDock(button, Dock.Bottom); body.Children.Add(button); body.Children.Add(list); dialog.Content = body; return dialog.ShowDialog() == true ? list.SelectedItem?.ToString() : null;
    }
    private void JavaPage()
    {
        var card = Card(ContentPanel, "サーバーごとにJavaを使い分ける"); card.Children.Add(Text("Temurin JREを専用フォルダへ導入します。システムのJava / PATHは変更しません。"));
        var versions = new ComboBox { ItemsSource = new[] { 8, 11, 17, 21, 25 }, SelectedItem = 21 }; card.Children.Add(versions);
        var list = new ListBox { Height = 170 }; void Refresh() { list.ItemsSource = Downloads.DiscoverJava(Path.Combine(store.Root, "java")).ToArray(); } Refresh();
        card.Children.Add(AsyncBtn("選択したJavaをダウンロード", async ct =>
        {
            var path = await downloads.InstallJava((int)versions.SelectedItem, Path.Combine(store.Root, "java"), Progress(), ct); Refresh(); list.SelectedItem = path;
        }, true)); card.Children.Add(list);
        var row = new WrapPanel(); row.Children.Add(Btn("再検出", Refresh)); row.Children.Add(Btn("選択サーバーに割り当て", () =>
        {
            var p = Selected ?? throw new InvalidOperationException("左側でサーバーを選んでください。"); Stopped(p); p.JavaPath = list.SelectedItem?.ToString() ?? throw new IOException("Javaを選択してください。"); store.Save(); status.Text = $"{p.Name} にJavaを割り当てました";
        }));
        row.Children.Add(AsyncBtn("Java バージョン確認", async ct =>
        {
            var path = list.SelectedItem?.ToString() ?? throw new IOException("Javaを選択してください。");
            using var child = new Process { StartInfo = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } }; child.StartInfo.ArgumentList.Add("-version"); child.Start();
            var output = child.StandardOutput.ReadToEndAsync(ct); var error = child.StandardError.ReadToEndAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(deadline.Token); } catch { child.Kill(true); throw; }
            MessageBox.Show(this, await output + await error, "Java");
        })); card.Children.Add(row);
    }
    private void ModsPage()
    {
        var p = Selected!; var root = store.ServerDir(p);
        var local = Card(ContentPanel, "MOD / プラグイン"); var folder = new ComboBox { ItemsSource = new[] { "mods", "plugins" }, SelectedItem = p.Engine is "paper" or "folia" ? "plugins" : "mods" }; JapaneseDisplay.Apply(folder); local.Children.Add(folder);
        var list = new ListBox { Height = 440, Name = "InstalledJars" }; local.Children.Add(list);
        string Target() => Path.Combine(root, folder.SelectedItem.ToString()!);
        void Refresh() { Directory.CreateDirectory(Target()); list.ItemsSource = Directory.EnumerateFiles(Target()).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order().ToArray(); }
        folder.SelectionChanged += (_, _) => Refresh(); Refresh();
        var row = new WrapPanel(); row.Children.Add(Btn("JARを追加", () => { Stopped(p); var dialog = new OpenFileDialog { Filter = "MOD / Plugin|*.jar", Multiselect = true }; if (dialog.ShowDialog(this) != true) return; new ServerFiles(store, p, Runtime(p)).AddJars(folder.SelectedItem.ToString()!, dialog.FileNames); Refresh(); }));
        row.Children.Add(Btn("有効 / 無効", () => { Stopped(p); var name = list.SelectedItem?.ToString() ?? throw new IOException("ファイルを選択してください。"); new ServerFiles(store, p, Runtime(p)).ToggleJar(folder.SelectedItem.ToString()!, name); Refresh(); }));
        row.Children.Add(Btn("フォルダ", () => Open(Target()))); local.Children.Add(row);
        local.Children.Add(Btn("一覧を更新", Refresh));
    }
    private void PresetsPage()
    {
        var p = Selected!;
        var preset = Card(ContentPanel, "構成プリセット"); preset.Children.Add(Text("MOD・プラグイン本体と共通設定・ワールド別設定・スクリプトを保存。切替前に全体をバックアップします。"));
        var keepSettings = new CheckBox { Content = "現在の設定を維持（未配置の設定だけ追加）", IsChecked = true }; preset.Children.Add(keepSettings);
        preset.Children.Add(Text("チェックを外すと同名設定をプリセットの内容に戻します。プリセットにない設定・プラグインデータはどちらでも残ります。", 12));
        var presetRow = new WrapPanel(); presetRow.Children.Add(AsyncBtn("現在の構成を保存", async ct =>
        {
            Stopped(p); var name = Ask("プリセット名（英数字・日本語可）", "構成-" + DateTime.Now.ToString("MMdd-HHmm")); if (name == null) return;
            var files = new ServerFiles(store, p, Runtime(p)); await Task.Run(() => files.SavePreset(name), ct);
        }));
        presetRow.Children.Add(AsyncBtn("プリセットへ切り替え", async ct =>
        {
            Stopped(p); Directory.CreateDirectory(store.PresetDir(p)); var chosen = Choose("プリセット", Directory.EnumerateFiles(store.PresetDir(p), "*.zip").Select(f => Path.GetFileName(f)!)); if (chosen == null) return;
            var preserve = keepSettings.IsChecked == true;
            if (!Confirm("MOD・プラグインJARを切り替えます。" + (preserve ? "現在の設定を維持します。" : "同名設定をプリセットの内容で上書きします。") + "全体バックアップを作って続行しますか？")) return;
            var files = new ServerFiles(store, p, Runtime(p)); await Task.Run(() => files.ApplyPreset(chosen, preserve), ct);
        })); preset.Children.Add(presetRow);
    }
    private void ModSearchPage()
    {
        var p = Selected!; var root = store.ServerDir(p);
        var search = Card(ContentPanel, "Modrinthから検索・導入"); var query = Field(search, "MOD名（選択サーバーのMCバージョン・ローダーで検索）", ""); var hits = new ListBox { Height = 400 }; List<JsonNode> results = [];
        search.Children.Add(AsyncBtn("検索", async ct => { var nodes = await downloads.SearchMods(query.Text, p.Version, p.Engine, ct); results = nodes.Select(n => n!).ToList(); hits.ItemsSource = results.Select(n => n["title"]!.ToString() + "  —  " + n["description"]!.ToString()).ToArray(); })); search.Children.Add(hits);
        search.Children.Add(AsyncBtn("選択MODと必須依存を導入", async ct =>
        {
            Stopped(p); if (hits.SelectedIndex < 0) throw new IOException("検索結果を選択してください。");
            var releases = await downloads.ResolveMods(results[hits.SelectedIndex]["project_id"]!.ToString(), p.Version, p.Engine, ct);
            if (!Confirm("導入予定:\n" + string.Join("\n", releases.Select(n => n["name"]!.ToString())) + "\n\n既存MODとの互換性は作者の説明も確認してください。導入しますか？")) return;
            await downloads.InstallMods(releases, Path.Combine(root, p.Engine is "paper" or "folia" ? "plugins" : "mods"), Progress(), ct);
        }, true));
    }
    private void ModpacksPage()
    {
        var p = Selected!; var root = store.ServerDir(p);
        var packs = Card(ContentPanel, "Modrinth MODパック (.mrpack)"); packs.Children.Add(Text("空の新規サーバーへサーバー必須ファイルを取り込みます。クライアント専用・任意ファイルは除外。ローダー本体は別途導入します。"));
        packs.Children.Add(AsyncBtn("mrpackを取り込む", async ct =>
        {
            Stopped(p); var file = Pick("Modrinth pack|*.mrpack"); if (file == null) return;
            var deps = await downloads.ImportMrpack(file, root, Progress(), ct);
            var dependencies = JsonNode.Parse(deps)!; p.Version = dependencies["minecraft"]?.ToString() ?? p.Version;
            p.Engine = dependencies["fabric-loader"] != null ? "fabric" : dependencies["neoforge"] != null ? "neoforge" : dependencies["forge"] != null ? "forge" : dependencies["quilt-loader"] != null ? "quilt" : "custom"; store.Save();
            p.LoaderVersion = dependencies["fabric-loader"]?.ToString() ?? ""; store.Save();
            MessageBox.Show(this, "取り込み完了。必要ローダーのバージョン:\n" + deps + "\n起動設定からローダー本体を導入してください。Fabricはパック指定版を保存しました。", "MODパック");
        }));
    }
    private void AutoModpackPage()
    {
        var root = store.ServerDir(Selected!);
        var sync = Card(ContentPanel, "AutoModpack • クライアントへMOD構成を同期");
        var autoConfig = Path.Combine(root, "automodpack", "automodpack-server.json");
        sync.Children.Add(Text(File.Exists(autoConfig) ? "AutoModpackのサーバー設定を検出しました。設定ファイル画面で編集できます。" : "対応するAutoModpackをサーバーとクライアントへ導入します。初回起動後に生成されるサーバー設定を編集できます。"));
        sync.Children.Add(Text("同期対象ファイルと配布専用フォルダを設定して管理します。MOD・設定変更後はコンソールの「同期データを再生成」を実行してください。", 12));
        sync.Children.Add(Text("構成プリセットにはAutoModpackのサーバー設定も保存します。配布ファイル・鍵は全体バックアップで保管してください。", 12));
        var syncRow = new WrapPanel(); syncRow.Children.Add(Btn("同期の設定を開く", () => Navigate("modsettings"))); syncRow.Children.Add(Btn("コンソールへ", () => Navigate("console"))); syncRow.Children.Add(Btn("AutoModpack公式ガイド", () => Open("https://github.com/Skidamek/AutoModpack/blob/main/docs/quick-start.mdx"))); sync.Children.Add(syncRow);
    }
    private void PropertiesPage()
    {
        var p = Selected!; var path = SafeFiles.Inside(store.ServerDir(p), "server.properties");
        string? original = File.Exists(path) ? File.ReadAllText(path) : null;
        var values = new ServerProperties(original ?? $"motd=A Minecraft Server\nmax-players=20\ndifficulty=easy\ngamemode=survival\nforce-gamemode=false\nonline-mode=true\nwhite-list=false\nview-distance=10\nsimulation-distance=10\nserver-port={p.Port}\n").Values;
        var intro = Card(ContentPanel, "ゲーム・接続の設定");
        intro.Children.Add(Text("停止中に保存できます。変更した項目だけを反映し、変更前のファイルは履歴へ残します。適用には通常、サーバーの再起動が必要です。"));
        intro.Children.Add(Text("このバージョンのファイルにある項目を表示します。ワールド生成設定は既存ワールドを作り直しません。Paperの既存ワールドの難易度はコンソールで変更してください。", 12));
        intro.Children.Add(Text($"現在の起動ポートは {p.Port}。接続ポートをこの画面で変更すると起動設定にも反映します。", 12));
        var search = Field(intro, "項目を検索", ""); search.Name = "PropertySearch";
        var readers = new Dictionary<string, Func<string>>(); var initial = new Dictionary<string, string>(values);
        var rows = new List<(FrameworkElement Row, string Search)>();
        var groupCards = new Dictionary<string, FrameworkElement>();
        string selectedGroup = "基本"; var groups = new WrapPanel { Name = "PropertyCategories" }; intro.Children.Add(groups);
        foreach (var group in values.Select(kv => (Field: PropertyFields.For(kv.Key, kv.Value), kv.Value)).GroupBy(x => x.Field.Group))
        {
            var card = Card(ContentPanel, group.Key); groupCards[group.Key] = (FrameworkElement)card.Parent;
            foreach (var (field, value) in group)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; card.Children.Add(row);
                row.Children.Add(Text(field.Label, 14)); row.ToolTip = "設定ファイル内のキー: " + field.Key;
                if (field.Group == "追加・独自設定") row.Children.Add(Text(field.Key, 11));
                if (field.Kind == "bool" && value is "true" or "false")
                {
                    var control = new CheckBox { Content = "有効", IsChecked = value == "true", Tag = field.Key }; row.Children.Add(control);
                    readers[field.Key] = () => control.IsChecked == true ? "true" : "false";
                }
                else if (field.Options != null)
                {
                    var labels = new Dictionary<string, string> { ["peaceful"] = "ピースフル", ["easy"] = "イージー", ["normal"] = "ノーマル", ["hard"] = "ハード", ["survival"] = "サバイバル", ["creative"] = "クリエイティブ", ["adventure"] = "アドベンチャー", ["spectator"] = "スペクテイター" };
                    var choices = field.Options.Concat([value]).Distinct().Select(x => new KeyValuePair<string, string>(x, labels.GetValueOrDefault(x, JapaneseDisplay.Label(x)))).ToArray();
                    var control = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = value, Tag = field.Key }; row.Children.Add(control);
                    readers[field.Key] = () => control.SelectedValue?.ToString() ?? value;
                }
                else if (field.Kind == "password")
                {
                    var control = new PasswordBox { Password = value, Tag = field.Key, Padding = new Thickness(8) }; control.SetResourceReference(Control.BackgroundProperty, "Input"); control.SetResourceReference(Control.ForegroundProperty, "Ink"); row.Children.Add(control); readers[field.Key] = () => control.Password;
                }
                else
                {
                    var control = new TextBox { Text = value, Tag = field.Key }; row.Children.Add(control); readers[field.Key] = () => control.Text;
                    if (field.Kind == "number") row.Children.Add(Text($"範囲: {field.Min}〜{field.Max}", 11));
                }
                rows.Add((row, field.Key + " " + field.Label + " " + field.Group));
            }
        }
        void Filter()
        {
            foreach (var row in rows) row.Row.Visibility = row.Search.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (name, card) in groupCards) card.Visibility = search.Text.Length > 0
                ? (((StackPanel)((Border)card).Child).Children.OfType<StackPanel>().Any(r => r.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed)
                : name == selectedGroup ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in groups.Children.OfType<Button>()) button.FontWeight = button.Tag?.ToString() == selectedGroup ? FontWeights.Bold : FontWeights.Normal;
        }
        if (!groupCards.ContainsKey(selectedGroup)) selectedGroup = groupCards.Keys.FirstOrDefault() ?? "基本";
        foreach (var name in groupCards.Keys) { var button = Btn(name, () => { selectedGroup = name; search.Clear(); Filter(); }); button.Tag = name; groups.Children.Add(button); }
        search.TextChanged += (_, _) => Filter(); Filter();
        bool Dirty() => readers.Any(kv => kv.Value() != initial[kv.Key]);
        mayLeave = () => !Dirty() || Confirm("サーバー設定の未保存の編集を破棄しますか？");
        intro.Children.Add(Btn("サーバー設定を保存", () =>
        {
            var changes = readers.Where(kv => original == null || kv.Value() != initial[kv.Key]).ToDictionary(kv => kv.Key, kv => kv.Value());
            original = new ServerFiles(store, p, Runtime(p)).SaveServerProperties(original, changes);
            foreach (var key in readers.Keys) initial[key] = readers[key]();
            status.Text = "サーバー設定を保存しました（反映には再起動が必要な場合があります）";
        }, true));
        intro.Children.Add(Btn("テキスト編集へ", () => Navigate("files")));
    }
    private void ModSettingsPage()
    {
        var p = Selected!; var root = store.ServerDir(p);
        var intro = Card(ContentPanel, "MODの設定を項目ごとに編集");
        intro.Children.Add(Text("JSON・単純なTOMLの項目を表示します。登録済みの項目は日本語名で表示し、未登録は原文を併記します。設定ファイルのキーは変更しません。", 12));
        intro.Children.Add(Text("停止中に保存できます。MODのバージョンによって意味や許容値が異なるため、作者の説明も確認してください。配列の各項目は1行ずつ入力します。", 12));
        var paths = ModConfigurations.EditableFiles(root).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".json" or ".toml").ToArray();
        var list = new ComboBox { Name = "ModSettingsFiles", ItemsSource = paths }; JapaneseDisplay.Apply(list); intro.Children.Add(list);
        var form = new StackPanel(); ContentPanel.Children.Add(form);
        var readers = new Dictionary<string, Func<string>>(); var initial = new Dictionary<string, string>();
        string current = "", original = ""; bool loading = false;
        bool Dirty() => readers.Any(x => x.Value() != initial[x.Key]);
        void Load(string relative)
        {
            original = File.ReadAllText(SafeFiles.Inside(root, relative)); current = relative;
            readers.Clear(); initial.Clear(); form.Children.Clear();
            var document = new ModSettingDocument(original, Path.GetExtension(relative));
            var categories = new WrapPanel { Name = "ModSettingCategories" }; form.Children.Add(categories);
            var cards = new List<FrameworkElement>();
            foreach (var group in document.Fields.GroupBy(f => f.Group))
            {
                var groupName = group.Key.Length == 0 ? "基本" : string.Join(" › ", group.Key.Split(" / ").Select(x => ModSettingDocument.Known(x) ? ModSettingDocument.Label(x) : x));
                var card = Card(form, groupName); var border = (FrameworkElement)card.Parent; cards.Add(border);
                categories.Children.Add(Btn(groupName, () => { foreach (var item in cards) item.Visibility = item == border ? Visibility.Visible : Visibility.Collapsed; }));
                border.Visibility = cards.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var field in group)
                {
                    var label = ModSettingDocument.Label(field.Key);
                    var row = new StackPanel { Margin = new Thickness(0, 4, 0, 8), ToolTip = "設定ファイル内のキー: " + field.Key }; card.Children.Add(row);
                    row.Children.Add(Text(label, 14));
                    if (!ModSettingDocument.Known(field.Key)) row.Children.Add(Text(field.Key, 11));
                    if (field.Kind == "bool")
                    {
                        var control = new CheckBox { Content = "有効", IsChecked = field.Value == "true", Tag = field.Key }; row.Children.Add(control); readers[field.Id] = () => control.IsChecked == true ? "true" : "false";
                    }
                    else if (field.Kind == "text" && new[] { "password", "secret", "token" }.Any(k => field.Key.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        var control = new PasswordBox { Password = field.Value, Tag = field.Key, Padding = new Thickness(8) }; control.SetResourceReference(Control.BackgroundProperty, "Input"); control.SetResourceReference(Control.ForegroundProperty, "Ink"); row.Children.Add(control); readers[field.Id] = () => control.Password;
                    }
                    else
                    {
                        var control = Field(row, field.Kind == "lines" ? "1行に1項目（空欄で指定なし）" : field.Kind == "number" ? "数値" : "設定値", field.Value, field.Kind == "lines");
                        control.Tag = field.Key; readers[field.Id] = () => control.Text;
                    }
                    initial[field.Id] = field.Value;
                }
            }
            form.Children.Add(Text("表示されない複雑な値や未対応の形式は「詳細なテキスト編集」で確認できます。未変更の内容は保存時もそのまま保持します。", 12));
        }
        list.SelectionChanged += (_, _) =>
        {
            if (loading || list.SelectedItem is not string relative) return;
            if (Dirty() && !Confirm("MOD設定の未保存の編集を破棄しますか？")) { loading = true; list.SelectedItem = current; loading = false; return; }
            try { Load(relative); } catch (Exception ex) { readers.Clear(); initial.Clear(); form.Children.Clear(); form.Children.Add(Text("設定を表示できません: " + ex.Message)); }
        };
        mayLeave = () => !Dirty() || Confirm("MOD設定の未保存の編集を破棄しますか？");
        intro.Children.Add(Btn("MOD設定を保存", () =>
        {
            if (current.Length == 0) throw new IOException("設定ファイルを選択してください。");
            var changes = readers.Where(x => x.Value() != initial[x.Key]).ToDictionary(x => x.Key, x => x.Value());
            new ServerFiles(store, p, Runtime(p)).SaveModSettings(current, original, changes);
            Load(current); status.Text = "MOD設定を保存しました。通常は次回サーバー起動から反映されます。";
        }, true));
        intro.Children.Add(Btn("詳細なテキスト編集へ", () => Navigate("files")));
        list.SelectedItem = paths.FirstOrDefault(f => f.EndsWith("automodpack-server.json")) ?? paths.FirstOrDefault();
        if (paths.Length == 0) form.Children.Add(Text("対応形式の設定がありません。MODが初回起動で設定を生成した後に開き直してください。"));
    }
    private void FilesPage()
    {
        var p = Selected!; var root = store.ServerDir(p); ContentPanel.Children.Add(Btn("ゲーム・接続の設定を開く", () => Navigate("properties"))); var card = Card(ContentPanel, "設定ファイルを編集"); card.Children.Add(Text("停止中に保存できます。保存前のファイルは履歴に退避します。server-portは起動設定のポートが優先されます。"));
        if (p.Engine == "paper") card.Children.Add(Text("Paperの既存ワールドの難易度は、コンソールで difficulty hard などを送信して変更してください。設定ファイルだけでは既存ワールドへ反映されない場合があります。", 12));
        card.Children.Add(Text("FTBのSNBT、JSON5/JSONC、CFG、KubeJSのJS、CraftTweakerのZSにも対応。JSON以外の構文・値は各MOD側で検証されます。最大500件・1ファイル1MB未満。", 12));
        var paths = ModConfigurations.EditableFiles(root).ToList(); if (!paths.Contains("server.properties")) paths.Insert(0, "server.properties");
        var list = new ComboBox { ItemsSource = paths, SelectedIndex = 0 }; JapaneseDisplay.Apply(list); card.Children.Add(list);
        var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, Height = 340, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        string current = ""; bool dirty = false; bool loading = false;
        void Load() { loading = true; current = list.SelectedItem?.ToString() ?? "server.properties"; var path = SafeFiles.Inside(root, current); editor.Text = File.Exists(path) ? File.ReadAllText(path) : "online-mode=true\nserver-port=" + p.Port; dirty = false; loading = false; }
        list.SelectionChanged += (_, _) => { if (loading) return; if (dirty && !Confirm("未保存の編集を破棄して切り替えますか？")) { loading = true; list.SelectedItem = current; loading = false; return; } Load(); };
        editor.TextChanged += (_, _) => { if (!loading) dirty = true; }; Load(); card.Children.Add(editor);
        mayLeave = () => !dirty || Confirm("設定ファイルの未保存の編集を破棄して移動しますか？");
        card.Children.Add(Btn("ファイルを保存", () =>
        {
            new ServerFiles(store, p, Runtime(p)).SaveConfiguration(current, editor.Text); dirty = false; status.Text = "保存しました";
        }, true));
    }
    private void BackupsPage()
    {
        var p = Selected!; var card = Card(ContentPanel, "バックアップ"); card.Children.Add(Text("サーバー全体をZIPで保存します。整合性のため停止中のみ実行できます。復元前のフォルダは .previous-* として残ります。"));
        var mode = new ComboBox { ItemsSource = new[] { "高速（圧縮なし・容量大）", "圧縮（容量を節約）" }, SelectedIndex = 0 };
        card.Children.Add(Text("保存方式", 12)); card.Children.Add(mode);
        var progressBar = new ProgressBar { Name = "BackupProgress", Minimum = 0, Maximum = 100, Height = 16, Margin = new Thickness(0, 12, 0, 5) };
        var progressText = Text("バックアップ待機中", 12); card.Children.Add(progressBar); card.Children.Add(progressText);
        Directory.CreateDirectory(store.BackupDir(p)); var list = new ListBox { Height = 330 }; void Refresh() => list.ItemsSource = Directory.EnumerateFiles(store.BackupDir(p), "*.zip").OrderDescending().Select(f => Path.GetFileName(f)!).ToArray(); Refresh(); card.Children.Add(list);
        var row = new WrapPanel(); row.Children.Add(AsyncBtn("バックアップを作成", async ct =>
        {
            Stopped(p);
            progressBar.IsIndeterminate = true; progressText.Text = "ファイルを確認中…";
            var watch = Stopwatch.StartNew();
            var compression = mode.SelectedIndex == 0 ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
            var showingProgress = true;
            var reporter = new Progress<SnapshotProgress>(s =>
            {
                if (!showingProgress) return;
                progressBar.IsIndeterminate = false;
                progressBar.Value = s.BytesTotal == 0 ? (s.FilesTotal == 0 ? 100 : s.FilesDone * 100d / s.FilesTotal) : s.BytesDone * 100d / s.BytesTotal;
                var speed = s.BytesDone / Math.Max(watch.Elapsed.TotalSeconds, 0.1) / 1048576d;
                progressText.Text = $"{progressBar.Value:F0}%  •  {s.FilesDone}/{s.FilesTotal} ファイル  •  {s.BytesDone / 1048576d:F1}/{s.BytesTotal / 1048576d:F1} MB  •  {speed:F0} MB/s";
            });
            try
            {
                await Task.Run(() => SafeFiles.Snapshot(store.ServerDir(p), store.BackupDir(p), reporter, ct, compression), ct);
                showingProgress = false; progressBar.Value = 100; progressText.Text = $"完了  •  {watch.Elapsed.TotalSeconds:F1} 秒"; Refresh();
            }
            catch (OperationCanceledException) { showingProgress = false; progressBar.IsIndeterminate = false; progressText.Text = "キャンセルしました。未完成のZIPは削除しました。"; throw; }
        }, true));
        row.Children.Add(AsyncBtn("選択したバックアップを復元", async ct =>
        {
            Stopped(p); var file = list.SelectedItem?.ToString() ?? throw new IOException("バックアップを選択してください。"); if (!Confirm("選択したバックアップにサーバー全体を戻します。続行しますか？")) return;
            var old = await Task.Run(() => SafeFiles.Restore(SafeFiles.Inside(store.BackupDir(p), file), store.ServerDir(p)), ct); MessageBox.Show(this, "復元完了。直前のデータ:\n" + old + "\n起動プロファイルは変更されないため、Java・バージョン設定も確認してください。");
        })); row.Children.Add(Btn("保存先を開く", () => Open(store.BackupDir(p)), allowWhileBusy: true)); card.Children.Add(row);
    }
    private void SystemPage()
    {
        var card = Card(ContentPanel, "このPCの状態"); var info = new TextBox { Text = Diagnostics.Describe(), IsReadOnly = true, AcceptsReturn = true, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; card.Children.Add(info); card.Children.Add(Btn("更新", () => info.Text = Diagnostics.Describe()));
    }
    private void NetworkPage()
    {
        var network = Card(ContentPanel, "サーバーへの接続診断"); var host = Field(network, "ホスト名 / IP", "127.0.0.1"); var port = Field(network, "ポート", (Selected?.Port ?? 25565).ToString()); var result = Text(""); network.Children.Add(AsyncBtn("接続をテスト", async _ => { var number = int.Parse(port.Text); if (number is < 1 or > 65535) throw new IOException("ポートが不正です。"); result.Text = await Diagnostics.Probe(host.Text.Trim(), number); })); network.Children.Add(result);
        network.Children.Add(Btn("Windowsのファイアウォール設定", () => Open("windowsdefender://network/")));
    }
    private void AppearancePage()
    {
        var card = Card(ContentPanel, "自分に合った明るさで");
        card.Children.Add(Text("テーマを切り替えると、画面全体にすぐ反映します。次回起動時も選択を保持します。"));
        var row = new WrapPanel();
        row.Children.Add(Btn("ダーク", () => { Theme.Apply("Dark", true); status.Text = "ダークモードを保存しました"; }));
        row.Children.Add(Btn("ライト", () => { Theme.Apply("Light", true); status.Text = "ライトモードを保存しました"; }));
        card.Children.Add(row);
        card.Children.Add(Text("入力欄・選択リスト・チェックボックス・スクロールバー・アプリ内ダイアログに適用します。Windowsのファイル選択画面など、OSが提供する画面はWindows側の表示設定に従います。", 12));
        card.Children.Add(new Image { Source = Theme.Icon, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 12) });
        card.Children.Add(Text("CraftHelm  •  ブロックと灯台を組み合わせたアプリアイコン", 12));
    }
    private void HelpPage()
    {
        var card = Card(ContentPanel, "はじめに"); card.Children.Add(Text("サーバー追加 → 起動設定 → 本体導入 → Javaの導入・割り当て → EULA同意 → 起動。\nコンソールに Done が出たら接続できます。サーバーはアプリ終了前に停止してください。\n複数サーバーは異なるポートで起動してください。"));
        card.Children.Add(Text("Forge / NeoForge / Quilt / 独自JARは、事前導入したサーバーフォルダをコピーし、Javaと起動引数を設定します。CurseForge形式の自動解決、Bedrock専用サーバー、UPnP、自動スケジュール、遠隔操作は本版の対応外です。"));
        card.Children.Add(Text("MODは実行コードです。作者と対応環境を確認して導入してください。Modrinthの必須依存は解決しますが、既存MODとの全互換性は保証しません。\nバックアップ・ログの自動削除はしません。空き容量はシステム画面で確認できます。"));
        card.Children.Add(Btn("日本語ドキュメント（GitHub）", () => Open("https://github.com/ryuya0124/CraftHelm/tree/main/docs")));
        card.Children.Add(Btn("データフォルダ", () => Open(store.Root))); card.Children.Add(Text("保存先: " + store.Root, 12));
    }
    public void BeginUpdateChecks() => updater.Start();
    private void UpdatesPage()
    {
        var card = Card(ContentPanel, "GitHub Releasesから更新");
        card.Children.Add(Text("現在のバージョン: " + typeof(App).Assembly.GetName().Version!.ToString(3)));
        card.Children.Add(Text("起動後と24時間ごとに更新を確認し、ファイルをダウンロード・検証します。アプリを通常終了したあとに適用するため、稼働中のサーバーを自動停止しません。", 12));
        var enabled = new CheckBox { Content = "更新を自動取得し、アプリ終了後に適用", IsChecked = updater.Enabled };
        enabled.Click += (_, _) => { try { updater.SetEnabled(enabled.IsChecked == true); } catch (Exception ex) { Error(ex); } }; card.Children.Add(enabled);
        updateStatus = Text(updater.Status); card.Children.Add(updateStatus);
        card.Children.Add(AsyncBtn("今すぐ更新を確認", async _ => await updater.CheckAsync()));
        card.Children.Add(Btn("更新してアプリを再起動", () =>
        {
            if (!updater.IsReady) { status.Text = "更新のダウンロードが完了していません。"; return; }
            restartForUpdate = true; Close(); restartForUpdate = false;
        }, true));
        card.Children.Add(Btn("リリース情報", () => Open("https://github.com/ryuya0124/CraftHelm/releases")));
        card.Children.Add(Text("プレビューリリースも対象です。ZIP版は確認のみで、インストーラーから更新できます。通信に失敗してもサーバー管理は継続できます。", 12));
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (busy || runtimes.Values.Any(r => r.Busy || r.Running)) { e.Cancel = true; MessageBox.Show(this, "処理の完了と、CraftHelmから起動したサーバーの停止を確認してから閉じてください。", "CraftHelm"); return; }
        if (mayLeave != null && !mayLeave()) { e.Cancel = true; return; }
        if (updater.IsReady && (updater.Enabled || restartForUpdate))
        {
            try { updater.Schedule(restartForUpdate); }
            catch (Exception ex) { e.Cancel = true; Error(ex); return; }
        }
        updater.Dispose(); timer.Stop(); foreach (var runtime in runtimes.Values) runtime.Dispose(); downloads.Dispose();
    }
}
