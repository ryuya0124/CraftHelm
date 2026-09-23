using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CraftHarbor.Core;

public sealed class Downloads : IDisposable
{
    private readonly HttpClient http;
    public Downloads(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CraftHelm/0.1.10 (https://github.com/ryuya0124/CraftHarbor)");
    }
    public async Task<JsonNode> Json(string url, CancellationToken ct = default) => JsonNode.Parse(await http.GetStringAsync(url, ct)) ?? throw new IOException("空のAPI応答です。");
    public static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback) throw new IOException("HTTPSの公開ダウンロードURLが必要です。");
    }
    public async Task FileAsync(string url, string path, string? hash = null, string algorithm = "SHA256", IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ValidateUrl(url);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode(); ValidateUrl(response.RequestMessage!.RequestUri!.AbsoluteUri);
            var total = response.Content.Headers.ContentLength;
            const long limit = 4L * 1024 * 1024 * 1024;
            if (total > limit) throw new IOException("1ファイルの上限4GBを超えています。");
            using var digest = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long read = 0; int count; var last = DateTime.MinValue;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    read += count; if (read > limit) throw new IOException("ダウンロード上限を超えました。");
                    digest.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 250) { progress?.Report($"{Path.GetFileName(path)}  {read / 1048576d:F1} MB" + (total.HasValue ? $" / {total / 1048576d:F1} MB" : "")); last = DateTime.UtcNow; }
                }
                if (total.HasValue && total.Value != read) throw new IOException("ダウンロードが途中で終了しました。");
            }
            var actual = Convert.ToHexString(digest.GetHashAndReset());
            if (hash != null && !actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("ハッシュが一致しません。ファイルを採用しませんでした。");
            ct.ThrowIfCancellationRequested(); File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public async Task<string[]> MinecraftVersions(CancellationToken ct) => (await Json("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", ct))["versions"]!.AsArray().Where(n => (string?)n!["type"] == "release").Select(n => (string)n!["id"]!).ToArray();
    public async Task<string[]> ServerVersions(string engine, CancellationToken ct)
    {
        if (engine == "vanilla") return await MinecraftVersions(ct);
        IEnumerable<string> supported;
        switch (engine)
        {
            case "fabric":
                var games = await Json("https://meta.fabricmc.net/v2/versions/game", ct);
                supported = games.AsArray().Where(n => (bool?)n!["stable"] == true).Select(n => n!["version"]!.ToString());
                break;
            case "paper": case "folia":
                var project = await Json($"https://fill.papermc.io/v3/projects/{engine}", ct);
                supported = project["versions"]!.AsObject().SelectMany(group => group.Value!.AsArray()).Select(n => n!.ToString());
                break;
            case "quilt":
                var quilt = await Json("https://meta.quiltmc.org/v3/versions/game", ct);
                supported = quilt.AsArray().Where(n => (bool?)n!["stable"] == true).Select(n => n!["version"]!.ToString());
                break;
            case "forge":
                var xml = await http.GetStringAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", ct);
                supported = XDocument.Parse(xml).Descendants("version").Select(n => n.Value.Split('-')[0]);
                break;
            case "neoforge":
                var neo = await Json("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct);
                supported = neo["versions"]!.AsArray().Select(n => n!.ToString().Split('.')).Where(parts => parts.Length >= 3 && int.TryParse(parts[0], out var major) && major >= 20)
                    .Select(parts => int.Parse(parts[0]) >= 26 ? $"{parts[0]}.{parts[1]}" : $"1.{parts[0]}.{parts[1]}");
                break;
            default:
                throw new NotSupportedException("この種類は導入済みサーバーに合わせてMinecraftバージョンを入力してください。");
        }
        // Use Mojang release ordering, excluding snapshots and duplicate provider entries.
        var available = supported.ToHashSet(StringComparer.Ordinal);
        return (await MinecraftVersions(ct)).Where(available.Contains).ToArray();
    }
    public async Task<int> InstallServer(ServerProfile p, string directory, IProgress<string> progress, CancellationToken ct)
    {
        var version = Uri.EscapeDataString(p.Version);
        string url; string? hash = null; var algorithm = "SHA256";
        var manifest = await Json("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", ct);
        var entry = manifest["versions"]!.AsArray().FirstOrDefault(n => (string?)n!["id"] == p.Version) ?? throw new IOException("Minecraftバージョンが見つかりません。");
        var details = await Json((string)entry["url"]!, ct);
        var java = (int?)details["javaVersion"]?["majorVersion"] ?? 8;
        switch (p.Engine)
        {
            case "vanilla":
                var server = details["downloads"]?["server"] ?? throw new IOException("このバージョンにはサーバー配布がありません。");
                url = (string)server["url"]!; hash = (string)server["sha1"]!; algorithm = "SHA1"; break;
            case "paper": case "folia": case "velocity":
                var builds = await Json($"https://fill.papermc.io/v3/projects/{p.Engine}/versions/{version}/builds", ct);
                var build = builds.AsArray().FirstOrDefault(n => (string?)n!["channel"] == "STABLE") ?? throw new IOException("指定バージョンの安定ビルドがありません。");
                var download = build["downloads"]!["server:default"]!;
                url = (string)download["url"]!; hash = (string?)download["checksums"]?["sha256"]; break;
            case "fabric":
                var loaders = await Json($"https://meta.fabricmc.net/v2/versions/loader/{version}", ct);
                var loader = loaders.AsArray().FirstOrDefault(n => string.IsNullOrWhiteSpace(p.LoaderVersion) ? (bool?)n!["loader"]!["stable"] == true : n!["loader"]!["version"]!.ToString() == p.LoaderVersion)?["loader"]?["version"]?.ToString() ?? throw new IOException("指定のFabricローダーがありません。");
                var installers = await Json("https://meta.fabricmc.net/v2/versions/installer", ct);
                var installer = installers.AsArray().First(n => (bool?)n!["stable"] == true)!["version"]!.ToString();
                url = $"https://meta.fabricmc.net/v2/versions/loader/{version}/{loader}/{installer}/server/jar"; break;
            default: throw new IOException("この種類は既存サーバーを取り込んでください。");
        }
        await FileAsync(url, SafeFiles.Inside(directory, "server.jar"), hash, algorithm, progress, ct);
        p.Jar = "server.jar"; p.LaunchArgs = []; return java;
    }
    public async Task<string> InstallJava(int major, string root, IProgress<string> progress, CancellationToken ct)
    {
        if (major is not (8 or 11 or 17 or 21 or 25)) throw new IOException("対応Javaを選択してください。");
        var assets = await Json($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse", ct);
        if (assets.AsArray().Count == 0) throw new IOException("該当するJavaがありません。");
        var package = assets[0]!["binary"]!["package"]!;
        var target = Path.Combine(root, $"temurin-{major}-{Guid.NewGuid():N}");
        var zip = target + ".zip";
        try
        {
            await FileAsync((string)package["link"]!, zip, (string)package["checksum"]!, "SHA256", progress, ct);
            await Task.Run(() => SafeFiles.ExtractZip(zip, target, 2L * 1024 * 1024 * 1024), ct);
            return SafeFiles.Files(target).FirstOrDefault(f => Path.GetFileName(f).Equals("java.exe", StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("java.exeが見つかりません。");
        }
        finally { if (File.Exists(zip)) File.Delete(zip); }
    }
    public static IEnumerable<string> DiscoverJava(string root)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var homes = new[] { Environment.GetEnvironmentVariable("JAVA_HOME"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"), root };
        foreach (var home in homes.Where(h => !string.IsNullOrWhiteSpace(h) && Directory.Exists(h)))
        {
            foreach (var file in Directory.EnumerateFiles(home!, "java.exe", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })) found.Add(file);
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) { var file = Path.Combine(dir, "java.exe"); if (File.Exists(file)) found.Add(file); }
        return found;
    }
    public async Task<JsonArray> SearchMods(string query, string version, string loader, CancellationToken ct)
    {
        var kind = loader is "paper" or "folia" ? "plugin" : "mod";
        var facets = System.Text.Json.JsonSerializer.Serialize(new[] { new[] { "project_type:" + kind }, new[] { "versions:" + version }, new[] { "categories:" + loader }, new[] { "server_side:required", "server_side:optional" } });
        return (await Json($"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=30", ct))["hits"]!.AsArray();
    }
    public async Task<List<JsonNode>> ResolveMods(string project, string version, string loader, CancellationToken ct)
    {
        var resolved = new Dictionary<string, JsonNode>(); var visiting = new HashSet<string>();
        async Task Visit(string? id, string? pinned)
        {
            if (resolved.Count > 150) throw new IOException("依存MODが多すぎます。");
            JsonNode release;
            if (pinned != null) release = await Json("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(pinned), ct);
            else
            {
                var game = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { version }));
                var loaders = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { loader }));
                var versions = await Json($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(id!)}/version?game_versions={game}&loaders={loaders}&include_changelog=false", ct);
                release = versions.AsArray().FirstOrDefault(n => (string?)n!["version_type"] == "release") ?? throw new IOException($"{id}: 対応する正式リリースがありません。");
            }
            if (!release["game_versions"]!.AsArray().Any(n => n!.ToString() == version) || !release["loaders"]!.AsArray().Any(n => n!.ToString() == loader)) throw new IOException("依存MODのバージョン・ローダーが一致しません。");
            var key = release["project_id"]!.ToString();
            if (resolved.TryGetValue(key, out var previous)) { if (previous["id"]!.ToString() != release["id"]!.ToString()) throw new IOException("依存MODの指定バージョンが衝突しています。"); return; }
            if (!visiting.Add(key)) return;
            foreach (var dep in release["dependencies"]!.AsArray().Where(n => (string?)n!["dependency_type"] == "required"))
            {
                var depId = (string?)dep!["project_id"]; var pin = (string?)dep["version_id"];
                if (depId == null && pin == null) throw new IOException("外部依存MODは手動導入が必要です。");
                await Visit(depId, pin);
            }
            visiting.Remove(key); resolved[key] = release;
        }
        await Visit(project, null); return resolved.Values.ToList();
    }
    public async Task InstallMods(List<JsonNode> releases, string target, IProgress<string> progress, CancellationToken ct)
    {
        var stage = target + ".download-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage);
        try
        {
            foreach (var release in releases)
            {
                var files = release["files"]!.AsArray(); var file = files.FirstOrDefault(n => (bool?)n!["primary"] == true) ?? files[0]!;
                var filename = (string)file["filename"]!;
                if (!filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new IOException("JAR以外のMODです。");
                if (File.Exists(SafeFiles.Inside(target, filename)) || File.Exists(SafeFiles.Inside(stage, filename))) throw new IOException("同名MODが存在します。先に構成を確認してください。");
                await FileAsync((string)file["url"]!, SafeFiles.Inside(stage, filename), (string)file["hashes"]!["sha512"]!, "SHA512", progress, ct);
            }
            Directory.CreateDirectory(target);
            var moved = new List<string>();
            try { foreach (var file in SafeFiles.Files(stage)) { var dest = SafeFiles.Inside(target, Path.GetFileName(file)); File.Move(file, dest); moved.Add(dest); } }
            catch { foreach (var file in moved) File.Move(file, SafeFiles.Inside(stage, Path.GetFileName(file))); throw; }
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public async Task<string> ImportMrpack(string archive, string target, IProgress<string> progress, CancellationToken ct)
    {
        if (SafeFiles.Files(target).Any()) throw new IOException("MODパックは空の新規サーバーに取り込んでください。");
        var stage = target + ".pack-" + Guid.NewGuid().ToString("N");
        var unpack = stage + "-unpack";
        var preserveStage = false;
        try
        {
            await Task.Run(() => SafeFiles.ExtractZip(archive, unpack), ct);
            var index = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(SafeFiles.Inside(unpack, "modrinth.index.json"), ct))!;
            if ((int?)index["formatVersion"] != 1 || (string?)index["game"] != "minecraft") throw new IOException("未対応のMODパック形式です。");
            if (index["dependencies"] is not JsonObject dependencies || dependencies["minecraft"] is not JsonValue minecraft || !minecraft.TryGetValue<string>(out var gameVersion) || string.IsNullOrWhiteSpace(gameVersion)) throw new IOException("MODパックのMinecraft依存情報が不正です。");
            var dependencyJson = dependencies.ToJsonString(HarborStore.Json);
            Directory.CreateDirectory(stage);
            foreach (var file in index["files"]!.AsArray())
            {
                if ((string?)file!["env"]?["server"] is "unsupported" or "optional") continue;
                var urls = file["downloads"]!.AsArray();
                var url = urls.Select(n => n!.ToString()).FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "cdn.modrinth.com") ?? throw new IOException("このパックにはModrinth CDN以外の配布元があります。手動導入してください。");
                var path = file["path"]!.ToString();
                if (!(path.StartsWith("mods/") || path.StartsWith("config/") || path.StartsWith("resourcepacks/"))) throw new IOException("未対応のパック配置先: " + path);
                await FileAsync(url, SafeFiles.Inside(stage, path), (string)file["hashes"]!["sha512"]!, "SHA512", progress, ct);
            }
            foreach (var folder in new[] { "overrides", "server-overrides" })
            {
                var source = Path.Combine(unpack, folder);
                foreach (var file in SafeFiles.Files(source))
                {
                    var dest = SafeFiles.Inside(stage, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!); System.IO.File.Copy(file, dest, true);
                }
            }
            ct.ThrowIfCancellationRequested();
            try { SafeFiles.PromoteIntoEmptyDirectory(stage, target); }
            catch (AggregateException) { preserveStage = true; throw; }
            return dependencyJson;
        }
        finally { if (!preserveStage && Directory.Exists(stage)) Directory.Delete(stage, true); if (Directory.Exists(unpack)) Directory.Delete(unpack, true); }
    }
    public void Dispose() => http.Dispose();
}
