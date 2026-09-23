using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;

namespace CraftHarbor.Core;

public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新しいサーバー";
    public string Engine { get; set; } = "vanilla";
    public string Version { get; set; } = "1.21.1";
    public string LoaderVersion { get; set; } = "";
    public string JavaPath { get; set; } = "java";
    public int MinMemoryMb { get; set; } = 512;
    public int MaxMemoryMb { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public string Jar { get; set; } = "server.jar";
    public string[] JvmArgs { get; set; } = [];
    public string[] LaunchArgs { get; set; } = [];
    public bool EulaAccepted { get; set; }
    public override string ToString() => Name;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("名前を入力してください。");
        if (MinMemoryMb < 128 || MaxMemoryMb < MinMemoryMb || MaxMemoryMb > 1048576) throw new InvalidOperationException("メモリは128MB以上、最大値は最小値以上にしてください。");
        if (Port is < 1 or > 65535) throw new InvalidOperationException("ポートは1〜65535です。");
        if (string.IsNullOrWhiteSpace(JavaPath)) throw new InvalidOperationException("Javaを指定してください。");
    }
}

public sealed class HarborStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Root { get; }
    public List<ServerProfile> Profiles { get; }
    public HarborStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        var file = Path.Combine(Root, "profiles.json");
        Profiles = File.Exists(file) ? JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(file), Json) ?? [] : [];
        foreach (var p in Profiles) _ = ServerDir(p);
    }
    public string ServerDir(ServerProfile p) => SafeFiles.Inside(Path.Combine(Root, "servers"), p.Id);
    public string BackupDir(ServerProfile p) => SafeFiles.Inside(Path.Combine(Root, "backups"), p.Id);
    public string PresetDir(ServerProfile p) => SafeFiles.Inside(Path.Combine(Root, "presets"), p.Id);
    public void Save() => SafeFiles.AtomicWrite(Path.Combine(Root, "profiles.json"), JsonSerializer.Serialize(Profiles, Json));
    public ServerProfile Add(string name)
    {
        var p = new ServerProfile { Name = name };
        Directory.CreateDirectory(ServerDir(p)); Profiles.Add(p); Save(); return p;
    }
    public bool Remove(ServerProfile profile)
    {
        var index = Profiles.IndexOf(profile);
        if (index < 0) return false;
        Profiles.RemoveAt(index);
        try { Save(); return true; }
        catch { Profiles.Insert(index, profile); throw; }
    }
    public int ReconcileMissingServerFolders(Func<ServerProfile, bool>? keep = null)
    {
        var missing = Profiles.Where(p => !Directory.Exists(ServerDir(p)) && (keep == null || !keep(p))).ToArray();
        if (missing.Length == 0) return 0;
        var original = Profiles.ToArray();
        Profiles.RemoveAll(p => missing.Contains(p));
        try { Save(); return missing.Length; }
        catch { Profiles.Clear(); Profiles.AddRange(original); throw; }
    }
}

public readonly record struct SnapshotProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal);

public static class SafeFiles
{
    public static string Inside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new IOException("不正な相対パスです。");
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("フォルダ外へのアクセスを拒否しました。");
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンク先へのアクセスを拒否しました。");
        }
        return path;
    }
    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try { File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false)); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static IEnumerable<string> Files(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root)) { _ = Inside(root, Path.GetFileName(file)); yield return file; }
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            _ = Inside(root, Path.GetFileName(dir));
            foreach (var file in Files(dir)) yield return file;
        }
    }
    public static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Files(source))
        {
            var dest = Inside(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, false);
        }
    }
    public static void ExtractZip(string zip, string target, long maxBytes = 20L * 1024 * 1024 * 1024)
    {
        using var archive = ZipFile.OpenRead(zip);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total = checked(total + entry.Length);
            if (total > maxBytes || archive.Entries.Count > 100000) throw new IOException("ZIPの展開上限を超えました。");
            var path = Inside(target, entry.FullName);
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("ZIP内のシンボリックリンクは使えません。");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
            input.CopyTo(output);
        }
    }
    public static string Snapshot(string source, string destination, IProgress<SnapshotProgress>? progress = null,
        CancellationToken cancellationToken = default, CompressionLevel compression = CompressionLevel.Fastest)
    {
        var files = new List<(string Path, long Size)>();
        foreach (var file in Files(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add((file, new FileInfo(file).Length));
        }
        var total = files.Sum(f => f.Size);
        progress?.Report(new SnapshotProgress(0, files.Count, 0, total));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        var tmp = path + ".partial";
        try
        {
            long done = 0; int filesDone = 0; var lastReport = Stopwatch.GetTimestamp();
            var buffer = new byte[1024 * 1024];
            using (var archive = ZipFile.Open(tmp, ZipArchiveMode.Create))
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(Path.GetRelativePath(source, file.Path).Replace('\\', '/'), compression);
                    using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan);
                    using var output = entry.Open();
                    int read;
                    while ((read = input.Read(buffer)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read); done += read;
                        if (Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 100)
                        {
                            progress?.Report(new SnapshotProgress(filesDone, files.Count, done, total));
                            lastReport = Stopwatch.GetTimestamp();
                        }
                    }
                    filesDone++;
                    if (filesDone == files.Count || Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 100)
                    {
                        progress?.Report(new SnapshotProgress(filesDone, files.Count, done, total));
                        lastReport = Stopwatch.GetTimestamp();
                    }
                }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tmp, path); return path;
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    // Stage and validate before swapping. The old tree remains available for recovery.
    public static string Restore(string archive, string target)
    {
        var stage = target + ".restore-" + Guid.NewGuid().ToString("N");
        var old = target + ".previous-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        Directory.CreateDirectory(stage);
        try { ExtractZip(archive, stage); }
        catch { Directory.Delete(stage, true); throw; }
        var exists = Directory.Exists(target);
        if (exists) MoveDirectoryWithRetry(target, old);
        try { MoveDirectoryWithRetry(stage, target); }
        catch (Exception restoreError)
        {
            if (exists)
            {
                try { MoveDirectoryWithRetry(old, target); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException($"復元と巻き戻しに失敗しました。データは削除していません。元データ: {old} / 展開済み: {stage}", restoreError, rollbackError);
                }
            }
            throw;
        }
        return old;
    }
    public static void PromoteIntoEmptyDirectory(string stage, string target)
    {
        // Keep any pre-existing tree intact until the prepared tree is in place.
        if (Files(target).Any()) throw new IOException("空の新規サーバーを使用してください。");
        var old = target + ".empty-" + Guid.NewGuid().ToString("N");
        var exists = Directory.Exists(target);
        if (exists) MoveDirectoryWithRetry(target, old);
        try
        {
            // Recheck after rename so content arriving during preparation is retained.
            if (exists && Files(old).Any()) throw new IOException("取り込み先が処理中に変更されました。");
            MoveDirectoryWithRetry(stage, target);
        }
        catch (Exception promotionError)
        {
            if (exists)
            {
                try { MoveDirectoryWithRetry(old, target); }
                catch (Exception rollbackError) { throw new AggregateException($"取り込みと巻き戻しに失敗しました。元データ: {old} / 準備済み: {stage}", promotionError, rollbackError); }
            }
            throw;
        }
        // Retain the empty old tree; never recursively delete a concurrently changed tree.
    }
    private static void MoveDirectoryWithRetry(string source, string destination)
    {
        // Windows can temporarily deny renames while recently extracted files are
        // being scanned or a process is releasing handles. Retry only lock/access
        // errors, for a bounded interval, without deleting either directory.
        var deadline = Environment.TickCount64 + 3000;
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (Exception ex) when (OperatingSystem.IsWindows()
                && ex is IOException or UnauthorizedAccessException
                && (ex.HResult & 0xffff) is 5 or 32 or 33
                && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(Math.Min(50 * (attempt + 1), 250));
            }
        }
    }
    public static string SetProperty(string text, string key, string value)
    {
        if (key.IndexOfAny(['\r','\n','=']) >= 0 || value.IndexOfAny(['\r','\n']) >= 0) throw new ArgumentException("改行は使用できません。");
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var indices = lines.Select((line, i) => (line, i)).Where(x => x.line.TrimStart().StartsWith(key + "=", StringComparison.Ordinal)).Select(x => x.i).ToList();
        if (indices.Count == 0) lines.Add(key + "=" + value);
        else { lines[indices[0]] = key + "=" + value; foreach (var i in indices.Skip(1).Reverse()) lines.RemoveAt(i); }
        return string.Join(Environment.NewLine, lines);
    }
}
