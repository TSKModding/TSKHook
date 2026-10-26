namespace TSKHook;

/// <summary>Positive and negative results belong to one immutable translation index.</summary>
public sealed class RenderedTextCache
{
    private readonly UiTranslationIndex index;
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<(string, ChineseScript), string> results = new();
    private readonly Queue<(string, ChineseScript)> order = new();
    public RenderedTextCache(UiTranslationIndex index, int capacity = 4096)
    { this.index = index; this.capacity = Math.Max(1, capacity); }
    public int Count { get { lock (gate) return results.Count; } }
    public bool TryTranslate(string source, ChineseScript script, out string translated, out bool cached)
    {
        translated = null;
        cached = false;
        if (string.IsNullOrEmpty(source) || source.Length > 4096 || !CaptureTextFilter.ShouldCapture("$.rendered_text",source)) return false;
        lock (gate)
        {
            var key = (source, script);
            if (results.TryGetValue(key, out translated)) { cached = true; return translated != null; }
            if (UiRenderedTextTranslator.TryTranslate(source, index, out var value))
            {
                value = ChineseTextConverter.Convert(value, script);
                if (!string.Equals(value, source, StringComparison.Ordinal)) translated = value;
            }
            if (results.Count >= capacity) results.Remove(order.Dequeue());
            results.Add(key, translated);
            order.Enqueue(key);
            return translated != null;
        }
    }
}
