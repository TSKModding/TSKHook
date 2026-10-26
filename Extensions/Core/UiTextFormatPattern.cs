using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TSKHook;

/// <summary>Curated UI number/date templates, never arbitrary regex or whole-sentence wildcards.</summary>
public sealed class UiTextFormatPattern
{
    private static readonly Regex Tokens = new(@"\{([0-3])\}", RegexOptions.CultureInvariant);
    private readonly Regex pattern;
    private readonly string translation;

    public UiTextFormatPattern(string source, string translation)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 1024 || string.IsNullOrWhiteSpace(translation) ||
            !Regex.IsMatch(source, @"[\u3040-\u30ff\u3400-\u9fff]"))
            throw new ArgumentException("A UI template must contain Japanese literals and at most 1024 characters.");
        var sourceTokens = Tokens.Matches(source).Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var targetTokens = Tokens.Matches(translation).Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (sourceTokens.Length == 0 || !sourceTokens.SequenceEqual(targetTokens) ||
            Tokens.Replace(source, "").IndexOfAny(new[] { '{', '}' }) >= 0 ||
            Tokens.Replace(translation, "").IndexOfAny(new[] { '{', '}' }) >= 0)
            throw new ArgumentException("UI template placeholders must match and use {0}..{3} only.");
        var builder = new StringBuilder("\\A");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        foreach (Match token in Tokens.Matches(source))
        {
            builder.Append(Regex.Escape(source.Substring(offset, token.Index - offset)));
            var name = "p" + token.Groups[1].Value;
            // Numeric/date/time tokens only, with bounded length. No user text, tags or code.
            builder.Append(seen.Add(name) ? "(?<" + name + ">[0-9０-９][0-9０-９/.:：～~+＋,， −-]{0,63})" : "\\k<" + name + ">");
            offset = token.Index + token.Length;
        }
        builder.Append(Regex.Escape(source.Substring(offset))).Append("\\z");
        pattern = new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(20));
        this.translation = translation;
    }

    public bool TryTranslate(string source, out string result)
    {
        result = null;
        if (source == null || source.Length > 4096) return false;
        try
        {
            var match = pattern.Match(source);
            if (!match.Success) return false;
            result = Tokens.Replace(translation, token => match.Groups["p" + token.Groups[1].Value].Value);
            return true;
        }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
