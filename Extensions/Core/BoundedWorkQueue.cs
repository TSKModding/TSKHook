namespace TSKHook;

/// <summary>Main-thread queue. Repeated notifications replace the value, not its place in the queue.</summary>
public sealed class BoundedWorkQueue<TKey, TValue> where TKey : notnull
{
    private readonly int capacity;
    private readonly Queue<TKey> order = new();
    private readonly Dictionary<TKey, TValue> pending = new();
    public BoundedWorkQueue(int capacity) => this.capacity = Math.Max(1, capacity);
    public int Count => pending.Count;
    public bool Enqueue(TKey key, TValue value)
    {
        if (pending.ContainsKey(key)) { pending[key] = value; return true; }
        if (pending.Count >= capacity) return false;
        pending.Add(key, value);
        order.Enqueue(key);
        return true;
    }
    public bool TryDequeue(out TValue value)
    {
        if (order.Count == 0) { value = default; return false; }
        var key = order.Dequeue();
        value = pending[key];
        pending.Remove(key);
        return true;
    }
    public void Clear() { pending.Clear(); order.Clear(); }
}
