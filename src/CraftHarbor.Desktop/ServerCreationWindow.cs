using System.IO;
using System.Windows;
using System.Windows.Controls;
using CraftHarbor.Core;

namespace CraftHarbor.Desktop;

public sealed class ServerCreationWindow : Window
{
    private static readonly string[] Engines = ["vanilla", "paper", "fabric", "folia", "forge", "neoforge", "quilt", "custom"];
    private readonly Func<string, CancellationToken, Task<string[]>> versions;
    private readonly TextBox name = new() { Name = "NewServerName", Text = "マイサーバー" };
    private readonly ComboBox engine = new() { Name = "NewServerEngine", ItemsSource = Engines, SelectedItem = "vanilla" };
    private readonly ComboBox versionChoices = new() { Name = "NewServerVersions", MaxDropDownHeight = 280 };
    private readonly TextBox manualVersion = new() { Name = "NewServerVersion", Text = "1.21.1" };
    private readonly TextBox fabricLoader = new() { Name = "NewFabricLoader" };
    private readonly StackPanel fabricRow = new();
    private readonly TextBlock hint = new() { Name = "NewVersionStatus" };
    private CancellationTokenSource? loading;
    public string ServerName => name.Text.Trim();
    public string Engine => engine.SelectedItem?.ToString() ?? "vanilla";
    public string Version => manualVersion.Text.Trim();
    public string LoaderVersion => Engine == "fabric" ? fabricLoader.Text.Trim() : "";

    public ServerCreationWindow(Func<string, CancellationToken, Task<string[]>> versions)
    {
        this.versions = versions;
        Title = "サーバーを追加 — CraftHelm"; Width = 560; Height = 590; MinWidth = 500; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Theme.Attach(this);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(24) }; scroll.Content = body; Content = scroll;
        body.Children.Add(new TextBlock { Text = "新しいサーバー", FontSize = 23, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        AddLabel(body, "サーバー名"); body.Children.Add(name);
        AddLabel(body, "サーバーの種類・MODローダー"); JapaneseDisplay.Apply(engine); body.Children.Add(engine);
        AddLabel(body, "対応するMinecraftバージョン"); body.Children.Add(versionChoices);
        body.Children.Add(hint);
        AddLabel(body, "Minecraftバージョン（候補がない場合は手入力）"); body.Children.Add(manualVersion);
        AddLabel(fabricRow, "Fabricローダーバージョン（空欄で最新安定版）"); fabricRow.Children.Add(fabricLoader); body.Children.Add(fabricRow);
        body.Children.Add(new TextBlock { Text = "Forge・NeoForge・Quiltは既存サーバーフォルダを取り込んで利用します。候補は配布元の公開情報から取得します。", Margin = new Thickness(0, 4, 0, 16) });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        var create = new Button { Content = "サーバーを作成", IsDefault = true, Background = Theme.Brush("Accent"), Foreground = Theme.Brush("AccentInk") };
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(ServerName) || string.IsNullOrWhiteSpace(Version)) { hint.Text = "名前とMinecraftバージョンを入力してください。"; return; }
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(create); body.Children.Add(buttons);
        engine.SelectionChanged += (_, _) => { if (IsLoaded) _ = LoadVersionsAsync(); };
        versionChoices.SelectionChanged += (_, _) => { if (versionChoices.SelectedItem is string selected) manualVersion.Text = selected; };
        manualVersion.TextChanged += (_, _) =>
        {
            if (versionChoices.SelectedItem is string selected && selected != manualVersion.Text) versionChoices.SelectedItem = null;
        };
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); _ = LoadVersionsAsync(); };
        Closed += (_, _) => { loading?.Cancel(); loading?.Dispose(); };
    }

    private static void AddLabel(Panel parent, string label) => parent.Children.Add(new TextBlock { Text = label, Foreground = Theme.Brush("Label") });

    public async Task LoadVersionsAsync()
    {
        loading?.Cancel(); loading?.Dispose();
        var current = new CancellationTokenSource(); loading = current;
        var selectedEngine = Engine;
        fabricRow.Visibility = selectedEngine == "fabric" ? Visibility.Visible : Visibility.Collapsed;
        versionChoices.ItemsSource = null;
        versionChoices.IsEnabled = selectedEngine != "custom";
        if (selectedEngine == "custom") { hint.Text = "独自JARのMinecraftバージョンを入力してください。"; return; }
        hint.Text = JapaneseDisplay.Label(selectedEngine) + " の対応バージョンを取得中…";
        try
        {
            var result = await versions(selectedEngine, current.Token);
            if (current.IsCancellationRequested || Engine != selectedEngine) return;
            versionChoices.ItemsSource = result;
            if (result.Length > 0)
            {
                versionChoices.SelectedItem = result.Contains(manualVersion.Text) ? manualVersion.Text : result[0];
                hint.Text = $"{JapaneseDisplay.Label(selectedEngine)} の候補 {result.Length} 件。候補の選択または手入力ができます。";
            }
            else hint.Text = "候補がありません。バージョンを手入力できます。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!current.IsCancellationRequested) hint.Text = "取得できませんでした。バージョンを手入力してください: " + ex.Message; }
    }
}
