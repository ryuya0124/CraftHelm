using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace CraftHarbor.Desktop;

public static class ConfigurationSyntax
{
    private static readonly Dictionary<(string Kind, string Appearance), IHighlightingDefinition> Cache = [];

    public static IHighlightingDefinition? ForPath(string path)
    {
        var kind = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" or ".json5" or ".jsonc" => "JSON",
            ".toml" => "TOML",
            ".yml" or ".yaml" or ".properties" or ".cfg" or ".conf" or ".hocon" or ".snbt" => "設定",
            ".js" or ".zs" => "スクリプト",
            _ => null
        };
        if (kind == null) return null;
        var key = (kind, Theme.Appearance);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var dark = Theme.Appearance == "Dark";
        var xml = (kind switch
        {
            "JSON" => Json,
            "TOML" => Toml,
            "スクリプト" => Script,
            _ => KeyValue
        }).Replace("$NAME", kind)
          .Replace("$STRING", dark ? "#A8DC91" : "#216B36")
          .Replace("$NUMBER", dark ? "#F0BF82" : "#95520B")
          .Replace("$KEYWORD", dark ? "#CAA9FF" : "#7740A2")
          .Replace("$COMMENT", dark ? "#90A2B6" : "#596B7A")
          .Replace("$SECTION", dark ? "#79DDCD" : "#087C6D")
          .Replace("$KEY", dark ? "#83C9FF" : "#075A9B");
        using var reader = XmlReader.Create(new StringReader(xml));
        return Cache[key] = HighlightingLoader.Load(HighlightingLoader.LoadXshd(reader), HighlightingManager.Instance);
    }

    private const string Json = """
        <SyntaxDefinition name="$NAME" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Key" foreground="$KEY" />
          <Color name="String" foreground="$STRING" />
          <Color name="Number" foreground="$NUMBER" />
          <Color name="Keyword" foreground="$KEYWORD" />
          <Color name="Comment" foreground="$COMMENT" />
          <RuleSet name="QuotedString"><Span begin="\\" end="." /></RuleSet>
          <RuleSet>
            <Span color="Comment" begin="//" end="$" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="Key" ruleSet="QuotedString">
              <Begin>"(?=(?:\\.|[^"\\])*"\s*:)</Begin>
              <End>"</End>
            </Span>
            <Span color="String" ruleSet="QuotedString" begin="&quot;" end="&quot;" />
            <Span color="String" begin="'" end="'" />
            <Rule color="Keyword">\b(?:true|false|null|NaN|Infinity)\b</Rule>
            <Rule color="Number">-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string Toml = """
        <SyntaxDefinition name="$NAME" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Key" foreground="$KEY" />
          <Color name="String" foreground="$STRING" />
          <Color name="Number" foreground="$NUMBER" />
          <Color name="Keyword" foreground="$KEYWORD" />
          <Color name="Comment" foreground="$COMMENT" />
          <Color name="Section" foreground="$SECTION" />
          <RuleSet name="QuotedString"><Span begin="\\" end="." /></RuleSet>
          <RuleSet>
            <Span color="Comment" begin="#" end="$" />
            <Rule color="Section">^\s*\[\[?[^\]\r\n]+\]\]?</Rule>
            <Rule color="Key">^\s*[A-Za-z0-9_.-]+(?=\s*=)</Rule>
            <Span color="String" multiline="true" ruleSet="QuotedString" begin="&quot;&quot;&quot;" end="&quot;&quot;&quot;" />
            <Span color="String" multiline="true" begin="'''" end="'''" />
            <Span color="String" ruleSet="QuotedString" begin="&quot;" end="&quot;" />
            <Span color="String" begin="'" end="'" />
            <Rule color="Keyword">\b(?:true|false)\b</Rule>
            <Rule color="Number">\b\d+(?:\.\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string KeyValue = """
        <SyntaxDefinition name="$NAME" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Key" foreground="$KEY" />
          <Color name="String" foreground="$STRING" />
          <Color name="Number" foreground="$NUMBER" />
          <Color name="Keyword" foreground="$KEYWORD" />
          <Color name="Comment" foreground="$COMMENT" />
          <RuleSet name="QuotedString"><Span begin="\\" end="." /></RuleSet>
          <RuleSet>
            <Span color="Comment" begin="#" end="$" />
            <Rule color="Key">^\s*[^#:=\s][^:=\r\n]*(?=\s*[:=])</Rule>
            <Span color="String" ruleSet="QuotedString" begin="&quot;" end="&quot;" />
            <Span color="String" begin="'" end="'" />
            <Rule color="Keyword">\b(?:true|false|null)\b</Rule>
            <Rule color="Number">\b\d+(?:\.\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string Script = """
        <SyntaxDefinition name="$NAME" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="String" foreground="$STRING" />
          <Color name="Number" foreground="$NUMBER" />
          <Color name="Keyword" foreground="$KEYWORD" />
          <Color name="Comment" foreground="$COMMENT" />
          <RuleSet name="QuotedString"><Span begin="\\" end="." /></RuleSet>
          <RuleSet>
            <Span color="Comment" begin="//" end="$" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String" ruleSet="QuotedString" begin="&quot;" end="&quot;" />
            <Span color="String" begin="'" end="'" />
            <Rule color="Keyword">\b(?:const|let|var|function|return|if|else|import|export|true|false|null)\b</Rule>
            <Rule color="Number">\b\d+(?:\.\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
