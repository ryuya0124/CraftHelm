using System.IO;

namespace CraftHarbor.Desktop;

public sealed record ConfigurationFileEntry(string RelativePath, DateTime LastModifiedUtc)
{
    public string FileName => Path.GetFileName(RelativePath);
    public string Folder => Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') is { Length: > 0 } folder ? folder : "サーバー直下";
    public string Category
    {
        get
        {
            var parts = RelativePath.Replace('\\', '/').Split('/');
            if (parts.Length == 1) return "基本";
            return parts[0].ToLowerInvariant() switch
            {
                "config" or "defaultconfigs" => "MOD設定",
                "plugins" => "プラグイン",
                "kubejs" or "scripts" => "スクリプト",
                "automodpack" => "AutoModpack",
                _ when parts.Length >= 3 && parts[1].Equals("serverconfig", StringComparison.OrdinalIgnoreCase) => "ワールド",
                _ => "その他"
            };
        }
    }

    public static ConfigurationFileEntry FromPath(string root, string relative)
    {
        var path = CraftHarbor.Core.SafeFiles.Inside(root, relative);
        return new ConfigurationFileEntry(relative, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue);
    }
}
