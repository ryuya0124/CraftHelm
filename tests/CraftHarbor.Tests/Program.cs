using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CraftHarbor.Core;

if (args.Contains("--fake-server"))
{
    Console.OutputEncoding = new UTF8Encoding(false); Console.InputEncoding = new UTF8Encoding(false);
    Console.WriteLine("Done (0.1s)! For help, type help"); Console.Error.WriteLine("stderr-is-captured");
    string? command;
    while ((command = Console.ReadLine()) != null) { Console.WriteLine("echo:" + command); if (command == "stop") return; }
    return;
}

var root = Path.Combine(Path.GetTempPath(), "CraftHarbor.Tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> body) { try { await body(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; } }
void Check(bool condition, string reason = "Assertion failed") { if (!condition) throw new Exception(reason); }
void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
async Task ThrowsAsync<T>(Func<Task> body) where T : Exception { try { await body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Task Sync(Action action) { action(); return Task.CompletedTask; }
string Temp(string name) => Path.Combine(root, name);

await Test("Storage roundtrip, Unicode and atomic writes", () => Sync(() => { var store = new HarborStore(Temp("store")); var p = store.Add("日本語のワールド"); p.JvmArgs = ["-Dname=a b"]; store.Save(); var again = new HarborStore(Temp("store")); Check(again.Profiles[0].Name == p.Name && again.Profiles[0].JvmArgs[0] == "-Dname=a b"); Check(!Directory.EnumerateFiles(Temp("store"), "*.partial").Any()); }));
await Test("New server stores selected loader and version together", () => Sync(() =>
{
    var store = new HarborStore(Temp("new-server"));
    var fabric = store.Add(" Fabric の世界 ", "fabric", "1.20.1", "0.16.0");
    var neo = store.Add("NeoForge", "neoforge", "1.21.1", "unused");
    var saved = new HarborStore(store.Root).Profiles;
    Check(saved.Single(p => p.Id == fabric.Id).Name == "Fabric の世界" && saved.Single(p => p.Id == fabric.Id).LoaderVersion == "0.16.0");
    Check(saved.Single(p => p.Id == neo.Id).Engine == "neoforge" && saved.Single(p => p.Id == neo.Id).Version == "1.21.1" && saved.Single(p => p.Id == neo.Id).LoaderVersion == "");
    Throws<InvalidOperationException>(() => store.Add("invalid", "fabric", ""));
    Check(store.Profiles.Count == 2);
}));
await Test("Corrupt configuration is never silently replaced", () => Sync(() => { Directory.CreateDirectory(Temp("corrupt")); File.WriteAllText(Temp("corrupt/profiles.json"), "invalid"); Throws<System.Text.Json.JsonException>(() => new HarborStore(Temp("corrupt"))); Check(File.ReadAllText(Temp("corrupt/profiles.json")) == "invalid"); }));
await Test("Reject traversal, absolute paths and NTFS streams", () => Sync(() => { foreach (var bad in new[] { "../outside", "..\\outside", "C:\\outside", "file:stream", "" }) Throws<IOException>(() => SafeFiles.Inside(root, bad)); }));
await Test("Memory / port validation", () => Sync(() => { Throws<InvalidOperationException>(() => new ServerProfile { MinMemoryMb = 2048, MaxMemoryMb = 512 }.Validate()); Throws<InvalidOperationException>(() => new ServerProfile { Port = 65536 }.Validate()); }));
await Test("Arguments preserve spaces without shell interpretation", () => Sync(() => { var p = new ServerProfile { Jar = "folder with spaces/server.jar", JvmArgs = ["-Dname=a b"], JavaPath = "C:\\Java Home\\bin\\java.exe" }; var info = ServerRuntime.BuildStart(p, root); Check(!info.UseShellExecute); Check(info.ArgumentList.Contains("-Dname=a b")); Check(info.ArgumentList.Contains(SafeFiles.Inside(root, p.Jar))); }));
await Test("Forge argfiles are separate native arguments", () => Sync(() => { var info = ServerRuntime.BuildStart(new ServerProfile { LaunchArgs = ["@libraries/forge/win_args.txt", "nogui"] }, root); Check(info.ArgumentList.Contains("@libraries/forge/win_args.txt") && !info.ArgumentList.Contains("-jar")); }));
await Test("Property update preserves comments and removes duplicate key", () => Sync(() => { var text = SafeFiles.SetProperty("# comment\nserver-port=1\nonline-mode=true\nserver-port=2", "server-port", "25570"); Check(text.Contains("# comment") && text.Contains("online-mode=true") && text.Split("server-port=").Length == 2); Throws<ArgumentException>(() => SafeFiles.SetProperty(text, "motd", "a\nb")); }));
await Test("Backup restores full tree and preserves pre-restore data", () => Sync(() => { var source = Temp("world"); Directory.CreateDirectory(Path.Combine(source, "world")); File.WriteAllText(Path.Combine(source, "world/level.dat"), "before"); var zip = SafeFiles.Snapshot(source, Temp("backups")); File.WriteAllText(Path.Combine(source, "world/level.dat"), "after"); File.WriteAllText(Path.Combine(source, "extra.txt"), "extra"); var old = SafeFiles.Restore(zip, source); Check(File.ReadAllText(Path.Combine(source, "world/level.dat")) == "before"); Check(!File.Exists(Path.Combine(source, "extra.txt"))); Check(File.ReadAllText(Path.Combine(old, "world/level.dat")) == "after"); }));
await Test("Missing server folder removes only its profile", () => Sync(() => { var root = Temp("missing-profile"); var store = new HarborStore(root); var keep = store.Add("keep"); var gone = store.Add("gone"); File.WriteAllText(Path.Combine(store.ServerDir(keep), "world.txt"), "untouched"); Directory.Delete(store.ServerDir(gone)); Check(store.ReconcileMissingServerFolders(p => p.Id == gone.Id) == 0); Check(store.ReconcileMissingServerFolders() == 1); Check(store.Profiles.Count == 1 && store.Profiles[0].Id == keep.Id); Check(File.ReadAllText(Path.Combine(store.ServerDir(keep), "world.txt")) == "untouched"); Check(new HarborStore(root).Profiles.Count == 1); }));
await Test("Removing a server registration keeps its files and other profiles", () => Sync(() => { var root = Temp("remove-profile"); var store = new HarborStore(root); var removed = store.Add("removed"); var kept = store.Add("kept"); File.WriteAllText(Path.Combine(store.ServerDir(removed), "world.dat"), "keep on disk"); Check(store.Remove(removed)); Check(!store.Remove(removed)); Check(File.ReadAllText(Path.Combine(store.ServerDir(removed), "world.dat")) == "keep on disk"); Check(new HarborStore(root).Profiles.Single().Id == kept.Id); }));
await Test("Backup reports progress, cancels cleanly and restores no-compression archive", () => Sync(() => { var source = Temp("progress-world"); Directory.CreateDirectory(source); File.WriteAllBytes(Path.Combine(source, "world.dat"), new byte[4 * 1024 * 1024]); var backup = Temp("progress-backups"); var updates = new List<SnapshotProgress>(); var reporter = new InlineProgress<SnapshotProgress>(updates.Add); var zip = SafeFiles.Snapshot(source, backup, reporter, compression: CompressionLevel.NoCompression); Check(updates.Count > 1 && updates[^1].FilesDone == 1 && updates[^1].BytesDone == 4 * 1024 * 1024); using (var z = ZipFile.OpenRead(zip)) Check(z.Entries.Single().Length == 4 * 1024 * 1024); using var cancel = new CancellationTokenSource(); Throws<OperationCanceledException>(() => SafeFiles.Snapshot(source, backup, new InlineProgress<SnapshotProgress>(p => { if (p.BytesDone > 0) cancel.Cancel(); }), cancel.Token, CompressionLevel.NoCompression)); Check(Directory.GetFiles(backup, "*.partial").Length == 0 && Directory.GetFiles(backup, "*.zip").Length == 1); }));
await Test("ZIP slip rejected with original tree untouched", () => Sync(() => { var zip = Temp("evil.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("../escape.txt").Open()); w.Write("bad"); } var target = Temp("safe"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep"), "original"); Throws<IOException>(() => SafeFiles.Restore(zip, target)); Check(File.ReadAllText(Path.Combine(target, "keep")) == "original"); Check(!File.Exists(Temp("escape.txt"))); }));
await Test("ZIP expanded size limit", () => Sync(() => { var zip = Temp("large.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("large.txt").Open()); w.Write(new string('x', 1000)); } Throws<IOException>(() => SafeFiles.ExtractZip(zip, Temp("large"), 10)); }));
await Test("ZIP duplicate paths rejected", () => Sync(() => { var zip = Temp("duplicate.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { z.CreateEntry("same"); z.CreateEntry("same"); } Throws<IOException>(() => SafeFiles.ExtractZip(zip, Temp("duplicate"))); }));
await Test("Reject unencrypted downloads", () => Sync(() => { Throws<IOException>(() => Downloads.ValidateUrl("http://example.com/a")); Throws<IOException>(() => Downloads.ValidateUrl("https://localhost/a")); }));
var payload = Encoding.UTF8.GetBytes("verified bytes"); var hash = Convert.ToHexString(SHA256.HashData(payload));
await Test("Verified download promotes completed file", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); await d.FileAsync("https://example.com/a", Temp("download.bin"), hash); Check(File.ReadAllBytes(Temp("download.bin")).SequenceEqual(payload)); });
await Test("Hash mismatch preserves existing destination", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); File.WriteAllText(Temp("existing.bin"), "original"); await ThrowsAsync<IOException>(() => d.FileAsync("https://example.com/a", Temp("existing.bin"), new string('0', 64))); Check(File.ReadAllText(Temp("existing.bin")) == "original"); Check(!Directory.EnumerateFiles(root, "*.partial").Any()); });
await Test("HTTP failure does not create destination", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound))); await ThrowsAsync<HttpRequestException>(() => d.FileAsync("https://example.com/a", Temp("missing.bin"))); Check(!File.Exists(Temp("missing.bin"))); });
await Test("Download cancellation does not promote file", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); using var cancel = new CancellationTokenSource(); cancel.Cancel(); await ThrowsAsync<OperationCanceledException>(() => d.FileAsync("https://example.com/a", Temp("cancel.bin"), ct: cancel.Token)); Check(!File.Exists(Temp("cancel.bin"))); });
await Test("Runtime logs are bounded", () => Sync(() => { using var runtime = new ServerRuntime(); for (var i = 0; i < 5000; i++) runtime.Append("line " + i); Check(runtime.History.Length == 2000 && runtime.History[^1].Contains("4999")); Check(runtime.Drain().Count == 250); }));
await Test("Runtime refuses missing EULA and missing JAR", () => Sync(() => { using var runtime = new ServerRuntime(); Throws<InvalidOperationException>(() => runtime.Start(new ServerProfile(), root, Temp("logs"))); Throws<FileNotFoundException>(() => runtime.Start(new ServerProfile { EulaAccepted = true }, root, Temp("logs"))); }));
await Test("Port collision never touches existing listener", async () => { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); try { using var runtime = new ServerRuntime(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; Throws<IOException>(() => runtime.Start(new ServerProfile { Port = port, EulaAccepted = true, LaunchArgs = ["--fake-server"] }, root, Temp("logs"))); Check((await Diagnostics.Probe("127.0.0.1", port)).Contains("成功")); } finally { listener.Stop(); } });
await Test("Managed child: stdout, stderr, command, graceful stop, restart", async () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var dir = Temp("process"); Directory.CreateDirectory(dir); using var runtime = new ServerRuntime();
    var profile = new ServerProfile { JavaPath = Environment.ProcessPath!, LaunchArgs = ["--fake-server"], EulaAccepted = true, Port = port };
    try
    {
        runtime.Start(profile, dir, Temp("process-logs"));
        var deadline = DateTime.UtcNow.AddSeconds(10); while (!runtime.Ready && DateTime.UtcNow < deadline) await Task.Delay(25);
        Check(runtime.Ready && runtime.Running); Throws<InvalidOperationException>(() => runtime.Start(profile, dir, Temp("process-logs")));
        await runtime.SendAsync("list"); await runtime.SendAsync("日本語コマンド"); await ThrowsAsync<ArgumentException>(() => runtime.SendAsync("list\nstop")); await runtime.StopAsync();
        Check(runtime.ExitCode == 0 && runtime.History.Any(x => x.Contains("echo:list")) && runtime.History.Any(x => x.Contains("stderr-is-captured")));
        Check(runtime.History.Any(x => x.Contains("echo:日本語コマンド")));
        runtime.Start(profile, dir, Temp("process-logs")); await runtime.StopAsync(); Check(runtime.ExitCode == 0);
    }
    finally { if (runtime.Running) runtime.Kill(); while (runtime.Busy) await Task.Delay(25); }
});
await Test("Mrpack skips client files and applies server overrides", async () =>
{
    var zip = Temp("sample.mrpack");
    using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        using (var w = new StreamWriter(z.CreateEntry("modrinth.index.json").Open())) w.Write("""{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.0"},"files":[{"path":"mods/client.jar","env":{"server":"unsupported"}},{"path":"mods/test.jar","downloads":["https://cdn.modrinth.com/test"],"hashes":{"sha512":"HASH"}}]}""".Replace("HASH", Convert.ToHexString(SHA512.HashData(payload))));
        using (var w = new StreamWriter(z.CreateEntry("overrides/config/test.txt").Open())) w.Write("common");
        using (var w = new StreamWriter(z.CreateEntry("server-overrides/config/test.txt").Open())) w.Write("server");
    }
    using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    var target = Temp("pack"); var deps = await d.ImportMrpack(zip, target, new Progress<string>(), default);
    Check(!File.Exists(Path.Combine(target, "mods/client.jar")) && File.Exists(Path.Combine(target, "mods/test.jar"))); Check(File.ReadAllText(Path.Combine(target, "config/test.txt")) == "server" && deps.Contains("fabric-loader"));
});
await Test("Mrpack refuses occupied server", async () => { using var d = new Downloads(); await ThrowsAsync<IOException>(() => d.ImportMrpack(Temp("sample.mrpack"), Temp("pack"), new Progress<string>(), default)); });

await Test("Version candidates use selected engine and exclude snapshots", async () =>
{
    var requests = new List<string>();
    using var d = new Downloads(new FakeHttp(request =>
    {
        var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
        var json = url.Contains("version_manifest") ? """{"versions":[{"id":"1.21.1","type":"release"},{"id":"24w01a","type":"snapshot"},{"id":"1.20.1","type":"release"},{"id":"1.12.2","type":"release"}]}"""
            : url.Contains("fabricmc") ? """[{"version":"1.21.1","stable":true},{"version":"24w01a","stable":false}]"""
            : url.EndsWith("/paper") ? """{"versions":{"1.21":["1.21.1","1.21.1"],"1.20":["1.20.1"]}}"""
            : url.Contains("quiltmc") ? """[{"version":"1.21.1","stable":true},{"version":"1.12.2","stable":false}]"""
            : url.Contains("minecraftforge") ? """<metadata><versioning><versions><version>1.21.1-52.0.1</version><version>1.20.1-47.0.0</version></versions></versioning></metadata>"""
            : url.Contains("neoforged") ? """{"versions":["21.1.1","20.1.100","0.25w14craftmine.3-beta"]}"""
            : """{"versions":{"1.20":["1.20.1"]}}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }));
    Check((await d.ServerVersions("vanilla", default)).SequenceEqual(new[] { "1.21.1", "1.20.1", "1.12.2" }));
    Check((await d.ServerVersions("fabric", default)).SequenceEqual(new[] { "1.21.1" }));
    Check((await d.ServerVersions("paper", default)).SequenceEqual(new[] { "1.21.1", "1.20.1" }));
    Check((await d.ServerVersions("folia", default)).SequenceEqual(new[] { "1.20.1" }));
    Check((await d.ServerVersions("quilt", default)).SequenceEqual(new[] { "1.21.1" }));
    Check((await d.ServerVersions("forge", default)).SequenceEqual(new[] { "1.21.1", "1.20.1" }));
    Check((await d.ServerVersions("neoforge", default)).SequenceEqual(new[] { "1.21.1", "1.20.1" }));
    var count = requests.Count;
    await ThrowsAsync<NotSupportedException>(() => d.ServerVersions("custom", default));
    Check(requests.Count == count, "Manual engines must not fetch Vanilla candidates");
});
await Test("Real Windows directory lock: restore waits, then succeeds without data loss", async () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var target = Temp("locked-world"); Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(target, "level.dat"), "backup-state");
    var archive = SafeFiles.Snapshot(target, Temp("locked-backups"));
    File.WriteAllText(Path.Combine(target, "level.dat"), "current-state");
    using var handle = new FileStream(Path.Combine(target, "level.dat"), FileMode.Open, FileAccess.Read, FileShare.Read);
    var restore = Task.Run(() => SafeFiles.Restore(archive, target));
    await Task.Delay(300);
    if (restore.IsFaulted) await restore;
    Check(!restore.IsCompleted, "Restore must wait while the directory is locked");
    handle.Dispose();
    var previous = await restore.WaitAsync(TimeSpan.FromSeconds(6));
    Check(File.ReadAllText(Path.Combine(target, "level.dat")) == "backup-state");
    Check(File.ReadAllText(Path.Combine(previous, "level.dat")) == "current-state");
});
await Test("Persistent Windows lock: restore fails within bound and retains both states", async () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var target = Temp("persistent-lock"); Directory.CreateDirectory(target);
    var level = Path.Combine(target, "level.dat"); File.WriteAllText(level, "backup-state");
    var archive = SafeFiles.Snapshot(target, Temp("persistent-backups"));
    File.WriteAllText(level, "current-state");
    using var handle = new FileStream(level, FileMode.Open, FileAccess.Read, FileShare.Read);
    var restore = Task.Run(() => SafeFiles.Restore(archive, target));
    try { await restore.WaitAsync(TimeSpan.FromSeconds(8)); throw new Exception("Locked restore unexpectedly succeeded"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    Check(File.ReadAllText(level) == "current-state");
    var stage = Directory.GetDirectories(root, "persistent-lock.restore-*").Single();
    Check(File.ReadAllText(Path.Combine(stage, "level.dat")) == "backup-state");
    Check(File.Exists(archive));
});
await Test("Empty preset preserves configuration, world and backup", () => Sync(() =>
{
    var store = new HarborStore(Temp("empty-preset")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime);
    files.SavePreset("empty"); files.SaveConfiguration("config/test.json", "{}"); files.SaveConfiguration("world/level.dat", "world");
    var backup = files.ApplyPreset("empty.zip"); Check(File.Exists(backup)); Check(File.Exists(Path.Combine(store.ServerDir(p), "config/test.json"))); Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "world/level.dat")) == "world");
}));
await Test("MOD config catalog and presets preserve schemas, extras and plugin data", () => Sync(() =>
{
    var store = new HarborStore(Temp("mod-settings")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime); var dir = store.ServerDir(p);
    var paths = new[] { "config/ftbchunks.snbt", "config/lithium.properties", "config/sample.json5", "config/sample.jsonc", "config/legacy.cfg", "defaultconfigs/create-server.toml", "Adventure/serverconfig/mekanism.toml", "kubejs/server_scripts/recipes.js", "scripts/recipes.zs", "plugins/Chunky/config.yml", ModConfigurations.AutoModpack };
    foreach (var path in paths) files.SaveConfiguration(path, path.EndsWith(".json") ? "{}" : "# original 日本語\nvalue = 1");
    files.SaveConfiguration("config/custom.unknown", "opaque-settings"); files.SaveConfiguration("plugins/Example/players.db", "player-data");
    files.SaveConfiguration("automodpack/private.key", "secret"); files.SaveConfiguration("Adventure/level.dat", "world");
    var visible = ModConfigurations.EditableFiles(dir).Select(x => x.Replace('\\', '/')).ToArray();
    Check(paths.All(visible.Contains)); Check(!visible.Any(x => x.Contains("private") || x.EndsWith("level.dat") || x.EndsWith(".db")));
    var jar = Temp("settings.jar"); File.WriteAllText(jar, "jar"); files.AddJars("mods", [jar]);
    var preset = files.SavePreset("saved"); using (var zip = ZipFile.OpenRead(preset))
    {
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        Check(paths.All(names.Contains)); Check(names.Contains("config/custom.unknown")); Check(!names.Any(x => x.EndsWith(".db") || x.EndsWith(".key") || x.EndsWith("level.dat")));
    }
    foreach (var path in paths) files.SaveConfiguration(path, path.EndsWith(".json") ? "{\"changed\":true}" : "changed");
    files.SaveConfiguration("config/added-later.toml", "keep"); files.ToggleJar("mods", "settings.jar");
    files.ApplyPreset("saved.zip");
    Check(paths.All(x => File.ReadAllText(Path.Combine(dir, x)).Contains("changed"))); Check(File.Exists(Path.Combine(dir, "mods/settings.jar"))); Check(!File.Exists(Path.Combine(dir, "mods/settings.jar.disabled")));
    files.ApplyPreset("saved.zip", false);
    Check(paths.All(x => !File.ReadAllText(Path.Combine(dir, x)).Contains("changed")));
    Check(File.ReadAllText(Path.Combine(dir, "config/added-later.toml")) == "keep"); Check(File.ReadAllText(Path.Combine(dir, "plugins/Example/players.db")) == "player-data"); Check(File.ReadAllText(Path.Combine(dir, "Adventure/level.dat")) == "world");
}));
await Test("Preset partial failure rolls back JARs and settings", () => Sync(() =>
{
    var store = new HarborStore(Temp("preset-rollback")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime); var dir = store.ServerDir(p);
    files.SaveConfiguration("config/blocked", "keep"); var jar = Temp("rollback.jar"); File.WriteAllText(jar, "old jar"); files.AddJars("mods", [jar]);
    Directory.CreateDirectory(store.PresetDir(p));
    using (var zip = ZipFile.Open(Path.Combine(store.PresetDir(p), "collision.zip"), ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("config/blocked/new.toml").Open()); w.Write("new"); }
    Throws<IOException>(() => files.ApplyPreset("collision.zip", false));
    Check(File.ReadAllText(Path.Combine(dir, "mods/rollback.jar")) == "old jar"); Check(File.ReadAllText(Path.Combine(dir, "config/blocked")) == "keep"); Check(Directory.GetFiles(store.BackupDir(p), "*.zip").Length == 1);
}));
await Test("Legacy preset merges settings and cannot overwrite world data", () => Sync(() =>
{
    var store = new HarborStore(Temp("legacy-merge")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime);
    files.SaveConfiguration("config/keep.txt", "current"); Directory.CreateDirectory(store.PresetDir(p));
    using (var zip = ZipFile.Open(Path.Combine(store.PresetDir(p), "legacy.zip"), ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("config/keep.txt").Open()); w.Write("old"); }
    files.ApplyPreset("legacy.zip"); Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "config/keep.txt")) == "current");
    using (var zip = ZipFile.Open(Path.Combine(store.PresetDir(p), "bad.zip"), ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("world/level.dat").Open()); w.Write("bad"); }
    Throws<IOException>(() => files.ApplyPreset("bad.zip", false)); Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "config/keep.txt")) == "current");
}));
await Test("All generated Minecraft 1.21.1 property keys have Japanese labels", () => Sync(() =>
{
    var keys = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "minecraft-1.21.1-property-keys.txt"));
    Check(keys.Length >= 60);
    foreach (var key in keys) Check(PropertyFields.For(key, "").Label != "追加設定（翻訳未登録）", "Missing translation: " + key);
}));
await Test("Legacy difficulty and game mode numbers map to readable choices", () => Sync(() =>
{
    string[] difficulties = ["peaceful", "easy", "normal", "hard"];
    string[] modes = ["survival", "creative", "adventure", "spectator"];
    for (var i = 0; i < 4; i++)
    {
        Check(PropertyFields.VisibleChoice("difficulty", i.ToString()) == difficulties[i]);
        Check(PropertyFields.VisibleChoice("gamemode", i.ToString()) == modes[i]);
    }
    Check(PropertyFields.VisibleChoice("difficulty", "hard") == "hard");
    Check(PropertyFields.VisibleChoice("other", "3") == "3");
}));
await Test("MOD JSON field edits preserve Unicode offsets, unknown data and arrays", () => Sync(() =>
{
    const string source = "{\"日本語\":\"保持\", \"server\": {\"enabled\":true, \"modpackName\":\"old\", \"syncedFiles\":[\"mods/**\"]}, \"unknown\":[1,{\"x\":2}], \"DO_NOT_CHANGE_IT\":3}";
    var doc = new ModSettingDocument(source, ".json");
    Check(doc.Fields.All(f => f.Key != "DO_NOT_CHANGE_IT" && f.Key != "x"));
    var changes = doc.Fields.Where(f => f.Key is "modpackName" or "syncedFiles").ToDictionary(f => f.Id, f => f.Key == "modpackName" ? "日本語の構成" : "mods/**\nconfig/**");
    var updated = doc.Apply(changes); var parsed = JsonNode.Parse(updated)!;
    Check(parsed["server"]!["modpackName"]!.ToString() == "日本語の構成" && parsed["server"]!["syncedFiles"]!.AsArray().Count == 2);
    Check(updated.StartsWith("{\"日本語\":\"保持\", ") && updated.Contains("\"unknown\":[1,{\"x\":2}], \"DO_NOT_CHANGE_IT\":3"));
    Check(new ModSettingDocument(updated, ".json").Fields.Single(f => f.Key == "enabled").Group == "server");
    Throws<IOException>(() => doc.Apply(new Dictionary<string, string> { ["missing"] = "1" }));
}));
await Test("MOD TOML value edits retain comments and reject unsafe scalar input", () => Sync(() =>
{
    const string source = "# original\r\n[general]\r\nenabled = true # keep\r\nport = 1234\r\nunknown = [1, 2]\r\nname = \"日本語\"\r\n";
    var doc = new ModSettingDocument(source, ".toml");
    var updated = doc.Apply(new Dictionary<string, string> { [doc.Fields.Single(f => f.Key == "enabled").Id] = "false" });
    Check(updated == source.Replace("true", "false"));
    Throws<IOException>(() => doc.Apply(new Dictionary<string, string> { [doc.Fields.Single(f => f.Key == "port").Id] = "1\nenabled=false" }));
    Check(new ModSettingDocument("text = \"\"\"\nenabled = true\n\"\"\"", ".toml").Fields.Count == 0);
}));
await Test("MOD settings save has stale-file protection and configuration history", () => Sync(() =>
{
    var store = new HarborStore(Temp("mod-form")); var p = store.Add("test"); using var runtime = new ServerRuntime();
    var relative = Path.Combine("config", "test.json"); var path = Path.Combine(store.ServerDir(p), relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    const string original = "{\"enabled\":true,\"extra\":42}"; File.WriteAllText(path, original);
    var doc = new ModSettingDocument(original, ".json"); var changes = new Dictionary<string, string> { [doc.Fields.Single(f => f.Key == "enabled").Id] = "false" };
    var files = new ServerFiles(store, p, runtime); files.SaveModSettings(relative, original, changes);
    Check(File.ReadAllText(path) == original.Replace("true", "false") && SafeFiles.Files(Path.Combine(store.Root, "file-history")).Any());
    Throws<IOException>(() => files.SaveModSettings(relative, original, changes));
}));
await Test("Properties escape/continuation parsing and changed-key-only patching", () => Sync(() =>
{
    var original = "# keep\r\n! comment\\\r\nmotd : \\u65e5\\u672c\\\r\n  \\u8a9e\r\ncustom\\:key = a\\=b\r\nmax-players=10\r\nmax-players:20\r\nunknown=unchanged";
    var document = new ServerProperties(original); Check(document.Values["motd"] == "日本語"); Check(document.Values["custom:key"] == "a=b"); Check(document.Values["max-players"] == "20");
    Check(document.Apply(new Dictionary<string,string>()) == original);
    var updated = document.Apply(new Dictionary<string,string> { ["motd"] = " 日本語\\path\nsecond", ["max-players"] = "3" });
    var read = new ServerProperties(updated); Check(read.Values["motd"] == " 日本語\\path\nsecond"); Check(read.Values["max-players"] == "3");
    Check(updated.Contains("# keep\r\n! comment\\\r\n")); Check(updated.Contains("custom\\:key = a\\=b\r\n")); Check(updated.EndsWith("unknown=unchanged"));
    Check(updated.Split("max-players").Length == 2); Throws<IOException>(() => new ServerProperties("motd=\\uBAD!"));
}));
await Test("GUI properties validation, port persistence, history and stale-file rejection", () => Sync(() =>
{
    var store = new HarborStore(Temp("properties-gui")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store,p,runtime);
    var original = "# note\nmotd=before\nserver-port=25565\nunknown=keep\n"; files.SaveConfiguration("server.properties", original);
    Throws<IOException>(() => files.SaveServerProperties(original, new Dictionary<string,string> { ["server-port"]="70000" }));
    Check(File.ReadAllText(Path.Combine(store.ServerDir(p),"server.properties")) == original);
    var updated = files.SaveServerProperties(original, new Dictionary<string,string> { ["motd"]="日本語", ["server-port"]="25570", ["white-list"]="true" });
    Check(new HarborStore(store.Root).Profiles.Single().Port == 25570 && p.Port == 25570); Check(updated.Contains("unknown=keep"));
    Check(File.ReadAllText(SafeFiles.Files(Path.Combine(store.Root,"file-history")).Single()) == original);
    Throws<IOException>(() => files.SaveServerProperties(original, new Dictionary<string,string> { ["motd"]="stale" }));
    Throws<IOException>(() => files.SaveServerProperties(updated, new Dictionary<string,string> { ["level-name"]="../outside" }));
    Check(File.ReadAllText(Path.Combine(store.ServerDir(p),"server.properties")) == updated);
}));
await Test("Properties profile-save failure restores file and in-memory port", () => Sync(() =>
{
    var store = new HarborStore(Temp("properties-rollback")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store,p,runtime);
    var original = "server-port=25565\n"; files.SaveConfiguration("server.properties",original);
    using var locked = new FileStream(Path.Combine(store.Root,"profiles.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
    try { files.SaveServerProperties(original,new Dictionary<string,string> { ["server-port"]="25571" }); throw new Exception("Locked profile was overwritten"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    Check(p.Port == 25565); Check(File.ReadAllText(Path.Combine(store.ServerDir(p),"server.properties")) == original);
}));
await Test("Release updater selects newer complete previews and rejects drafts/older versions", () => Sync(() =>
{
    string Release(string version, bool draft = false, bool checksum = true) => System.Text.Json.JsonSerializer.Serialize(new { tag_name="v"+version, draft, prerelease=true, assets=new object[] { new { name=$"CraftHarbor-{version}-win-x64-setup.exe", id=1, size=2048 }, new { name=checksum ? "SHA256SUMS.txt" : "other", id=2, size=200 } } });
    var json = "[" + string.Join(",", Release("0.1.8"), Release("0.2.0"), Release("9.0.0",true), Release("8.0.0",checksum:false)) + "]";
    Check(ReleaseUpdates.Select(json,new Version(0,1,7,0))?.Version == "0.2.0");
    Check(ReleaseUpdates.Select(json,new Version(0,2,0,0)) == null);
}));
await Test("Updater checksum requires exact asset and verifies downloaded size/content", () => Sync(() =>
{
    var path=Temp("update.exe"); File.WriteAllText(path,"test update bytes");
    var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    Check(ReleaseUpdates.ExpectedHash(hash+"  update.exe\n", "update.exe") == hash);
    Check(ReleaseUpdates.Verify(path,hash,new FileInfo(path).Length) == hash);
    Throws<IOException>(() => ReleaseUpdates.ExpectedHash(hash+"  wrong.exe", "update.exe"));
    Throws<IOException>(() => ReleaseUpdates.ExpectedHash(hash+"  update.exe\n"+hash+"  update.exe", "update.exe"));
    Throws<IOException>(() => ReleaseUpdates.Verify(path,new string('0',64),new FileInfo(path).Length));
    Throws<IOException>(() => ReleaseUpdates.Verify(path,hash,1));
}));
await Test("Rename updater prefers new name and keeps previous-name releases readable", () => Sync(() =>
{
    const string json = "[{\"tag_name\":\"v0.1.10\",\"draft\":false,\"assets\":[{\"name\":\"CraftHelm-0.1.10-win-x64-setup.exe\",\"id\":1,\"size\":2048},{\"name\":\"CraftHarbor-0.1.10-win-x64-setup.exe\",\"id\":2,\"size\":2048},{\"name\":\"SHA256SUMS.txt\",\"id\":3}]}]";
    Check(ReleaseUpdates.Select(json, new Version(0, 1, 9))?.InstallerId == 1);
    Check(ReleaseUpdates.Select(json.Replace("CraftHelm-0.1.10", "other-0.1.10"), new Version(0, 1, 9))?.InstallerId == 2);
}));
await Test("Configuration invalid JSON and traversal preserve original/history", () => Sync(() =>
{
    var store = new HarborStore(Temp("config-failure")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime);
    files.SaveConfiguration("config/a.json", "{}"); Throws<System.Text.Json.JsonException>(() => files.SaveConfiguration("config/a.json", "invalid")); Throws<IOException>(() => files.SaveConfiguration("../escape.txt", "bad"));
    Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "config/a.json")) == "{}"); Check(!SafeFiles.Files(Path.Combine(store.Root, "file-history")).Any());
    files.SaveConfiguration("config/a.json", "{\"ok\":true}"); Check(File.ReadAllText(SafeFiles.Files(Path.Combine(store.Root, "file-history")).Single()) == "{}");
}));
await Test("JAR duplicate batch rejects before copying, toggle collision preserves both", () => Sync(() =>
{
    var store = new HarborStore(Temp("jar-failures")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime);
    var a = Temp("a.jar"); var b = Temp("b.jar"); File.WriteAllText(a, "a"); File.WriteAllText(b, "b"); files.AddJars("mods", [a]);
    Throws<IOException>(() => files.AddJars("mods", [b, a])); Check(!File.Exists(Path.Combine(store.ServerDir(p), "mods/b.jar")));
    files.ToggleJar("mods", "a.jar"); files.AddJars("mods", [a]); Throws<IOException>(() => files.ToggleJar("mods", "a.jar.disabled"));
    Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "mods/a.jar.disabled")) == "a"); Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "mods/a.jar")) == "a");
    Throws<IOException>(() => files.AddJars("world", [a])); Throws<IOException>(() => files.ToggleJar("mods", "../a.jar"));
}));
await Test("Invalid preset root file cannot replace configuration or world", () => Sync(() =>
{
    var store = new HarborStore(Temp("invalid-preset")); var p = store.Add("test"); using var runtime = new ServerRuntime(); var files = new ServerFiles(store, p, runtime);
    files.SaveConfiguration("config/a.txt", "keep"); Directory.CreateDirectory(store.PresetDir(p));
    using (var zip = ZipFile.Open(Path.Combine(store.PresetDir(p), "invalid.zip"), ZipArchiveMode.Create)) { using var w = new StreamWriter(zip.CreateEntry("mods").Open()); w.Write("file, not directory"); }
    Throws<IOException>(() => files.ApplyPreset("invalid.zip")); Check(File.ReadAllText(Path.Combine(store.ServerDir(p), "config/a.txt")) == "keep");
}));
await Test("Mrpack accepts empty directories created by MOD screen", async () =>
{
    var target = Temp("pack-empty-folders"); Directory.CreateDirectory(Path.Combine(target, "mods"));
    using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    await d.ImportMrpack(Temp("sample.mrpack"), target, new Progress<string>(), default); Check(File.Exists(Path.Combine(target, "mods/test.jar")));
});
await Test("Mrpack missing dependency metadata cannot promote downloaded content", async () =>
{
    var archive = Temp("bad-deps.mrpack"); using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using (var w = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open())) w.Write("""{"formatVersion":1,"game":"minecraft","files":[]}"""); using (var w = new StreamWriter(zip.CreateEntry("overrides/config/probe.txt").Open())) w.Write("must not promote"); }
    var target = Temp("bad-deps-target"); using var d = new Downloads(); await ThrowsAsync<IOException>(() => d.ImportMrpack(archive, target, new Progress<string>(), default)); Check(!SafeFiles.Files(target).Any());
});
await Test("Required pinned dependencies resolve before parent and exclude optional", async () =>
{
    var requested = new List<string>();
    using var d = new Downloads(new FakeHttp(request =>
    {
        var url = request.RequestUri!.AbsolutePath; requested.Add(url);
        var json = url.EndsWith("/version/dep1") ? """{"id":"dep1","project_id":"dependency","game_versions":["1.21.1"],"loaders":["fabric"],"dependencies":[]}"""
            : """[{"id":"parent1","project_id":"parent","version_type":"release","game_versions":["1.21.1"],"loaders":["fabric"],"dependencies":[{"version_id":"dep1","dependency_type":"required"},{"project_id":"optional","dependency_type":"optional"}]}]""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }));
    var releases = await d.ResolveMods("parent", "1.21.1", "fabric", default);
    Check(releases.Select(r => r["id"]!.ToString()).SequenceEqual(new[] { "dep1", "parent1" })); Check(requested.Count == 2);
});
await Test("Pinned dependency wrong loader rejected", async () =>
{
    using var d = new Downloads(new FakeHttp(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/version/dep1")
        ? """{"id":"dep1","project_id":"dependency","game_versions":["1.21.1"],"loaders":["forge"],"dependencies":[]}"""
        : """[{"id":"parent1","project_id":"parent","version_type":"release","game_versions":["1.21.1"],"loaders":["fabric"],"dependencies":[{"version_id":"dep1","dependency_type":"required"}]}]""") }));
    await ThrowsAsync<IOException>(() => d.ResolveMods("parent", "1.21.1", "fabric", default));
});
await Test("Multi-MOD hash failure leaves target untouched", async () =>
{
    using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    JsonNode Release(string name, string digest) => System.Text.Json.JsonSerializer.SerializeToNode(new { files = new[] { new { primary = true, filename = name, url = "https://example.com/mod.jar", hashes = new { sha512 = digest } } } })!;
    var target = Temp("failed-mod-batch"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "existing.jar"), "keep");
    await ThrowsAsync<IOException>(() => d.InstallMods([Release("one.jar", Convert.ToHexString(SHA512.HashData(payload))), Release("two.jar", new string('0', 128))], target, new Progress<string>(), default));
    Check(Directory.GetFiles(target).Length == 1 && File.ReadAllText(Path.Combine(target, "existing.jar")) == "keep");
});
await Test("Cancelled mrpack leaves empty destination unchanged", async () =>
{
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var d = new Downloads();
    var target = Temp("cancel-pack"); Directory.CreateDirectory(Path.Combine(target, "mods"));
    await ThrowsAsync<OperationCanceledException>(() => d.ImportMrpack(Temp("sample.mrpack"), target, new Progress<string>(), cancel.Token)); Check(!SafeFiles.Files(target).Any());
});
await Test("Staged promotion refuses late destination file", () => Sync(() =>
{
    var stage = Temp("late-stage"); var target = Temp("late-target"); Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(stage, "new.txt"), "new"); File.WriteAllText(Path.Combine(target, "late.txt"), "keep");
    Throws<IOException>(() => SafeFiles.PromoteIntoEmptyDirectory(stage, target)); Check(File.ReadAllText(Path.Combine(target, "late.txt")) == "keep" && File.Exists(Path.Combine(stage, "new.txt")));
}));
await Test("Paper and Folia search plugin facets; Fabric searches mods", async () =>
{
    var queries = new List<string>(); using var d = new Downloads(new FakeHttp(request => { queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query)); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"hits\":[]}") }; }));
    foreach (var engine in new[] { "paper", "folia", "fabric" }) await d.SearchMods("test", "1.21.1", engine, default);
    Check(queries[0].Contains("project_type:plugin") && queries[1].Contains("project_type:plugin") && queries[2].Contains("project_type:mod"));
});
Console.WriteLine($"RESULT {passed} passed, {failed} failed");
Directory.Delete(root, true); Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var response = respond(request); response.RequestMessage = request; return Task.FromResult(response);
    }
}

sealed class InlineProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}
