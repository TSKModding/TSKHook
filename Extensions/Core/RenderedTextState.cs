namespace TSKHook;

/// <summary>Remembers the original text across our own writes, settings changes and package reloads.</summary>
public sealed class RenderedTextState
{
    private string original, displayed;
    private int revision = -1;
    private ChineseScript script;
    private bool enabled;
    public bool Resolve(string current, int newRevision, bool newEnabled, ChineseScript newScript,
        Func<string, string> translate, out string output)
    {
        output = current;
        var unchanged = string.Equals(current, displayed, StringComparison.Ordinal);
        if (unchanged && revision == newRevision && enabled == newEnabled && script == newScript) return false;
        if (!unchanged || revision == -1) original = current;
        output = newEnabled ? translate(original) ?? original : original;
        displayed = output; revision = newRevision; enabled = newEnabled; script = newScript;
        return !string.Equals(current, output, StringComparison.Ordinal);
    }
}
