using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CraftHarbor.Core;

public sealed class ServerProperties
{
    private sealed record Entry(string Raw, string? Key, string? Value);
    private readonly List<Entry> entries = [];
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    private readonly string newline;
    public ServerProperties(string text)
    {
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = Regex.Matches(text, @"[^\r\n]*(?:\r\n|\r|\n|$)").Select(m => m.Value).Where(x => x.Length > 0).ToArray();
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i]; var logical = raw.TrimEnd('\r', '\n').TrimStart(' ', '\t', '\f');
            if (logical.Length == 0 || logical[0] is '#' or '!') { entries.Add(new(raw, null, null)); continue; }
            while (logical.Length - logical.TrimEnd('\\').Length is var slashes && slashes % 2 == 1)
            {
                logical = logical[..^1];
                if (++i >= lines.Length) break;
                raw += lines[i]; logical += lines[i].TrimEnd('\r', '\n').TrimStart(' ', '\t', '\f');
            }
            int end = 0; bool escaped = false;
            for (; end < logical.Length; end++)
            {
                char c = logical[end];
                if (!escaped && (c is '=' or ':' or ' ' or '\t' or '\f')) break;
                escaped = !escaped && c == '\\';
            }
            int valueStart = end;
            while (valueStart < logical.Length && logical[valueStart] is ' ' or '\t' or '\f') valueStart++;
            if (valueStart < logical.Length && logical[valueStart] is '=' or ':') valueStart++;
            while (valueStart < logical.Length && logical[valueStart] is ' ' or '\t' or '\f') valueStart++;
            var key = Decode(logical[..end]); var value = Decode(logical[valueStart..]);
            entries.Add(new(raw, key, value)); Values[key] = value;
        }
    }
    private static string Decode(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\') { result.Append(c); continue; }
            if (++i >= value.Length) break;
            c = value[i];
            if (c == 'u')
            {
                if (i + 4 >= value.Length || !ushort.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw new IOException("propertiesのUnicodeエスケープが不正です。");
                result.Append((char)code); i += 4;
            }
            else result.Append(c switch { 't' => '\t', 'n' => '\n', 'r' => '\r', 'f' => '\f', _ => c });
        }
        return result.ToString();
    }
    private static string Encode(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value)
            if (c < 32 || c > 126) result.Append("\\u" + ((int)c).ToString("x4"));
            else { if (c is '\\' or '=' or ':' or '#' or '!' or ' ') result.Append('\\'); result.Append(c); }
        return result.ToString();
    }
    public string Apply(IReadOnlyDictionary<string, string> changes)
    {
        var pending = changes.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var result = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.Key == null || !changes.ContainsKey(entry.Key)) { result.Append(entry.Raw); continue; }
            if (pending.Remove(entry.Key, out var value)) result.Append(Encode(entry.Key)).Append('=').Append(Encode(value)).Append(newline);
        }
        foreach (var (key, value) in pending)
        {
            if (result.Length > 0 && result[^1] is not '\r' and not '\n') result.Append(newline);
            result.Append(Encode(key)).Append('=').Append(Encode(value)).Append(newline);
        }
        return result.ToString();
    }
}

public sealed record PropertyField(string Key, string Label, string Group, string Kind = "text", string[]? Options = null, long Min = 0, long Max = int.MaxValue)
{
    public void Validate(string value)
    {
        if (Kind == "bool" && value is not "true" and not "false") throw new IOException(Label + "は有効・無効を選んでください。");
        if (Kind == "number" && (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < Min || n > Max)) throw new IOException($"{Label}は{Min}〜{Max}の整数で指定してください。");
        if (Options != null && !Options.Contains(value)) throw new IOException(Label + "の選択値が不正です。");
    }
}

public static class PropertyFields
{
    public static string VisibleChoice(string key, string value)
    {
        string[] names = key switch
        {
            "difficulty" => ["peaceful", "easy", "normal", "hard"],
            "gamemode" => ["survival", "creative", "adventure", "spectator"],
            _ => []
        };
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0 && number < names.Length
            ? names[number] : value;
    }
    public static readonly PropertyField[] All = [
        new("motd", "サーバーの説明（MOTD）", "基本"), new("max-players", "最大プレイヤー数", "基本", "number", Min: 1),
        new("difficulty", "難易度", "基本", Options: ["peaceful", "easy", "normal", "hard", "0", "1", "2", "3"]),
        new("gamemode", "初期ゲームモード", "基本", Options: ["survival", "creative", "adventure", "spectator", "0", "1", "2", "3"]),
        new("force-gamemode", "参加時にゲームモードを統一", "基本", "bool"), new("hardcore", "ハードコア", "基本", "bool"),
        new("online-mode", "アカウント認証", "参加・権限", "bool"), new("white-list", "ホワイトリスト", "参加・権限", "bool"),
        new("enforce-whitelist", "ホワイトリストを強制", "参加・権限", "bool"), new("enforce-secure-profile", "安全なプロフィールを必須にする", "参加・権限", "bool"),
        new("pvp", "プレイヤー同士の攻撃", "参加・権限", "bool"), new("allow-flight", "飛行を許可", "参加・権限", "bool"),
        new("spawn-protection", "スポーン保護範囲", "参加・権限", "number"), new("player-idle-timeout", "放置時の退出（分・0で無効）", "参加・権限", "number"),
        new("op-permission-level", "管理者の権限レベル", "参加・権限", "number", Min: 0, Max: 4),
        new("view-distance", "描画距離（チャンク）", "負荷・通信", "number", Min: 2, Max: 32), new("simulation-distance", "演算距離（チャンク）", "負荷・通信", "number", Min: 2, Max: 32),
        new("max-tick-time", "応答監視（ms・-1で無効）", "負荷・通信", "number", Min: -1, Max: long.MaxValue),
        new("pause-when-empty-seconds", "無人時に停止するまでの秒数", "負荷・通信", "number", Min: -1),
        new("network-compression-threshold", "通信圧縮のしきい値", "負荷・通信", "number", Min: -1),
        new("server-port", "接続ポート（起動設定にも反映）", "負荷・通信", "number", Min: 1, Max: 65535), new("server-ip", "待受IP（空欄で全インターフェース）", "負荷・通信"),
        new("level-name", "ワールドフォルダ名", "ワールド生成"), new("level-seed", "ワールドのシード", "ワールド生成"), new("level-type", "ワールド生成タイプ", "ワールド生成"),
        new("generate-structures", "構造物を生成", "ワールド生成", "bool"), new("spawn-monsters", "モンスターの出現", "ワールド生成", "bool"), new("spawn-animals", "動物の出現", "ワールド生成", "bool"),
        new("allow-nether", "ネザーを有効にする", "ワールド生成", "bool"), new("enable-command-block", "コマンドブロック", "参加・権限", "bool"),
        new("enable-rcon", "RCONを有効にする", "管理・配布", "bool"), new("rcon.port", "RCONポート", "管理・配布", "number", Min: 1, Max: 65535), new("rcon.password", "RCONパスワード", "管理・配布", "password"),
        new("enable-query", "Queryを有効にする", "管理・配布", "bool"), new("query.port", "Queryポート", "管理・配布", "number", Min: 1, Max: 65535),
        new("resource-pack", "リソースパックURL", "管理・配布"), new("require-resource-pack", "リソースパックを必須にする", "管理・配布", "bool"),
        new("accepts-transfers", "他サーバーからの転送接続を許可", "参加・権限", "bool"),
        new("broadcast-console-to-ops", "コンソールの実行結果を管理者へ通知", "管理・配布", "bool"),
        new("broadcast-rcon-to-ops", "遠隔コマンドの実行結果を管理者へ通知", "管理・配布", "bool"),
        new("bug-report-link", "不具合報告ページのアドレス", "管理・配布"),
        new("enable-jmx-monitoring", "Javaの外部監視を有効にする", "管理・配布", "bool"),
        new("enable-status", "サーバー一覧への状態応答", "参加・権限", "bool"),
        new("entity-broadcast-range-percentage", "生物・物体の通知範囲（%）", "負荷・通信", "number", Min: 10, Max: 1000),
        new("function-permission-level", "データパック関数の権限レベル", "参加・権限", "number", Max: 4),
        new("generator-settings", "ワールド生成の詳細条件", "ワールド生成"),
        new("hide-online-players", "参加中プレイヤーの一覧を非公開にする", "参加・権限", "bool"),
        new("initial-disabled-packs", "新規ワールドで無効にするデータパック", "ワールド生成"),
        new("initial-enabled-packs", "新規ワールドで有効にするデータパック", "ワールド生成"),
        new("log-ips", "接続元のIPアドレスをログに記録", "管理・配布", "bool"),
        new("max-chained-neighbor-updates", "連鎖するブロック更新の上限", "負荷・通信", "number", Min: -1),
        new("max-world-size", "ワールドの最大半径（ブロック）", "ワールド生成", "number", Min: 1, Max: 29999984),
        new("prevent-proxy-connections", "プロキシ経由と判定した接続を拒否", "参加・権限", "bool"),
        new("rate-limit", "毎秒の通信パケット数制限（0で無効）", "負荷・通信", "number"),
        new("region-file-compression", "ワールド保存の圧縮方式", "負荷・通信", Options: ["deflate", "lz4", "none"]),
        new("resource-pack-id", "配布リソースパックの識別番号", "管理・配布"),
        new("resource-pack-prompt", "リソースパック導入時の案内文", "管理・配布"),
        new("resource-pack-sha1", "リソースパックの検証用ハッシュ", "管理・配布"),
        new("spawn-npcs", "村人などの出現", "ワールド生成", "bool"),
        new("sync-chunk-writes", "チャンクの同期書き込み", "負荷・通信", "bool"),
        new("text-filtering-config", "チャットフィルターの接続設定", "管理・配布"),
        new("text-filtering-version", "チャットフィルターの方式番号", "管理・配布", "number"),
        new("use-native-transport", "OS固有の通信処理を使用", "負荷・通信", "bool")
    ];
    public static PropertyField For(string key, string value) => All.FirstOrDefault(f => f.Key == key)
        ?? new(key, "追加設定（翻訳未登録）", "追加・独自設定", key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) ? "password" : value is "true" or "false" ? "bool" : "text");
}
