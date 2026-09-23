using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CraftHarbor.Core;

namespace CraftHarbor.Desktop;

public sealed class UpdateManager : IDisposable
{
    private sealed record Preferences(bool Enabled = true);
    private readonly string preferencesPath;
    private readonly ReleaseUpdates releases = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(24) };
    private bool checking;
    private (string Path, string Hash)? prepared;
    private long preparedSize;
    public bool Enabled { get; private set; } = true;
    public bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
    public bool IsReady => IsInstalled && prepared is { } update && File.Exists(update.Path);
    public string Status { get; private set; } = "更新はまだ確認していません。";
    public event Action? Changed;
    public UpdateManager(string root)
    {
        preferencesPath = Path.Combine(root, "updates-preferences.json");
        try { if (File.Exists(preferencesPath)) Enabled = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencesPath))?.Enabled ?? true; }
        catch { Enabled = false; Status = "自動更新の設定を読み込めませんでした。更新画面で設定し直してください。"; }
        timer.Tick += (_, _) => { if (Enabled) _ = CheckAsync(); };
    }
    public void SetEnabled(bool value)
    {
        SafeFiles.AtomicWrite(preferencesPath, JsonSerializer.Serialize(new Preferences(value)));
        Enabled = value; Changed?.Invoke();
    }
    public void Start()
    {
        if (!IsInstalled) { Status = "ZIP版です。自動適用はインストーラー版で利用できます。"; Changed?.Invoke(); return; }
        timer.Start(); if (Enabled) _ = CheckAsync();
    }
    private void Report(string value) { Status = value; Changed?.Invoke(); }
    public async Task CheckAsync()
    {
        if (checking) return;
        checking = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            Report("GitHub Releasesを確認しています…");
            var current = typeof(App).Assembly.GetName().Version!;
            var candidate = await releases.CheckAsync(current, timeout.Token);
            if (candidate == null) { prepared = null; Report("最新版です（" + current.ToString(3) + "）。"); return; }
            if (!IsInstalled) { Report($"v{candidate.Version}があります。GitHub Releasesのインストーラーで更新してください。"); return; }
            Report($"v{candidate.Version}をダウンロード・検証しています…");
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CraftHarbor", "updates", candidate.Version);
            prepared = await releases.DownloadAsync(candidate, cache, timeout.Token); preparedSize = candidate.Size;
            Report($"v{candidate.Version}の準備完了。自動更新が有効なら、アプリの通常終了後に適用します。");
        }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) Report("更新の確認がタイムアウトしました。あとで再試行できます。"); }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) Report("更新を確認できませんでした: " + ex.Message); }
        finally { checking = false; }
    }
    public void Schedule(bool restart)
    {
        if (!IsInstalled || prepared is not { } update) throw new IOException("適用できる更新がありません。");
        ReleaseUpdates.Verify(update.Path, update.Hash, preparedSize);
        var directory = Path.GetDirectoryName(update.Path)!;
        var id = Guid.NewGuid().ToString("N"); var script = Path.Combine(directory, "apply-" + id + ".ps1");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "UpdateHelper.ps1"), script);
        using var process = Process.GetCurrentProcess();
        var manifest = Path.Combine(directory, "apply-" + id + ".json");
        SafeFiles.AtomicWrite(manifest, JsonSerializer.Serialize(new { Installer = update.Path, SHA256 = update.Hash, Size = preparedSize, ParentId = process.Id, ParentStartedTicks = process.StartTime.ToUniversalTime().Ticks, TargetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), Restart = restart }));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Manifest", manifest }) info.ArgumentList.Add(argument);
        Process.Start(info)?.Dispose();
    }
    public void Dispose() { timer.Stop(); lifetime.Cancel(); releases.Dispose(); lifetime.Dispose(); }
}
