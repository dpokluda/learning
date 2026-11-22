namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter11
{
    [TestMethod]
    public void Lru_cache_evicts_least_recently_used_item_in_constant_time()
    {
        LruCache<string, string> cache = new LruCache<string, string>(capacity: 2);
        cache.Put("k1", "v1");
        cache.Put("k2", "v2");

        // Touch k1 so k2 becomes the least recently used entry
        Assert.IsTrue(cache.TryGet("k1", out string? val1));
        Assert.AreEqual("v1", val1);

        cache.Put("k3", "v3");

        Assert.IsFalse(cache.TryGet("k2", out _));
        Assert.IsTrue(cache.TryGet("k1", out _));
        Assert.IsTrue(cache.TryGet("k3", out _));
    }

    [TestMethod]
    public void Cache_lease_token_prevents_slow_reader_from_poisoning_cache_after_concurrent_delete()
    {
        LeaseProtectedCache cache = new LeaseProtectedCache();

        // 1. Reader misses cache and acquires Lease Token 1, then reads old value "v1" from DB
        (string? cachedValue, long leaseToken) = cache.GetOrAcquireLease("config:eu");
        Assert.IsNull(cachedValue);

        // 2. Concurrent writer updates DB to "v2" and invalidates the cache key!
        cache.InvalidateOnWrite("config:eu");

        // 3. Slow reader resumes and attempts to populate cache with stale "v1" using invalidated leaseToken
        bool leaseSetAccepted = cache.TrySetWithLease("config:eu", "stale-v1", leaseToken);
        Assert.IsFalse(leaseSetAccepted);

        // 4. Subsequent fresh reader misses cleanly, gets new lease, and populates "v2"
        (string? afterStaleAttempt, long freshLease) = cache.GetOrAcquireLease("config:eu");
        Assert.IsNull(afterStaleAttempt);
        Assert.IsTrue(cache.TrySetWithLease("config:eu", "fresh-v2", freshLease));
        Assert.AreEqual("fresh-v2", cache.GetOrAcquireLease("config:eu").Value);
    }
}

file sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new LinkedList<(TKey, TValue)>();

    public LruCache(int capacity)
    {
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? existing))
        {
            _list.Remove(existing);
            existing.Value = (key, value);
            _list.AddFirst(existing);
            return;
        }

        if (_map.Count >= _capacity && _list.Last is not null)
        {
            LinkedListNode<(TKey Key, TValue Value)> victim = _list.Last;
            _list.RemoveLast();
            _map.Remove(victim.Value.Key);
        }

        LinkedListNode<(TKey, TValue)> newNode = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(newNode);
        _map[key] = newNode;
    }
}

file sealed class LeaseProtectedCache
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _validLeases = new Dictionary<string, long>(StringComparer.Ordinal);
    private long _nextLeaseToken;

    public (string? Value, long LeaseToken) GetOrAcquireLease(string key)
    {
        if (_values.TryGetValue(key, out string? existing))
        {
            return (existing, 0);
        }

        _nextLeaseToken++;
        _validLeases[key] = _nextLeaseToken;
        return (null, _nextLeaseToken);
    }

    public void InvalidateOnWrite(string key)
    {
        _values.Remove(key);
        _validLeases.Remove(key);
    }

    public bool TrySetWithLease(string key, string value, long leaseToken)
    {
        if (!_validLeases.TryGetValue(key, out long activeToken) || activeToken != leaseToken)
        {
            return false;
        }

        _validLeases.Remove(key);
        _values[key] = value;
        return true;
    }
}
