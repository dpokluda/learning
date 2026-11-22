namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter12
{
    [TestMethod]
    public void Token_bucket_allows_burst_up_to_capacity_and_refills_linearly_over_time()
    {
        TokenBucketLimiter bucket = new TokenBucketLimiter(capacity: 3, refillTokensPerSecond: 2.0, initialTimeSeconds: 0.0);

        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));
        Assert.IsFalse(bucket.TryAcquire(cost: 1, nowSeconds: 0.0));

        // After 0.5 seconds at 2 tokens/sec, 1 token has refilled
        Assert.IsTrue(bucket.TryAcquire(cost: 1, nowSeconds: 0.5));
        Assert.IsFalse(bucket.TryAcquire(cost: 1, nowSeconds: 0.5));
    }

    [TestMethod]
    public void Sliding_window_counter_prevents_double_burst_at_fixed_window_boundary()
    {
        SlidingWindowCounterLimiter limiter = new SlidingWindowCounterLimiter(windowSeconds: 60, limit: 100);

        for (int i = 0; i < 80; i++)
        {
            Assert.IsTrue(limiter.TryAcquire(nowSeconds: 50.0));
        }

        // At t = 75s (15s into the second 60s window, alpha = 0.25), weighted previous is 80 * 0.75 = 60.
        // Therefore, only 40 new requests should be allowed at t = 75s!
        for (int i = 0; i < 40; i++)
        {
            Assert.IsTrue(limiter.TryAcquire(nowSeconds: 75.0));
        }

        Assert.IsFalse(limiter.TryAcquire(nowSeconds: 75.0));
    }

    [TestMethod]
    public void Circuit_breaker_transitions_closed_to_open_to_half_open_and_back_to_closed()
    {
        CircuitBreakerStateMachine breaker = new CircuitBreakerStateMachine(failureThreshold: 3, breakDurationMs: 5_000);
        Assert.AreEqual(CircuitState.Closed, breaker.State);

        breaker.RecordResult(succeeded: false, nowMs: 100);
        breaker.RecordResult(succeeded: false, nowMs: 200);
        breaker.RecordResult(succeeded: false, nowMs: 300);

        Assert.AreEqual(CircuitState.Open, breaker.State);
        Assert.IsFalse(breaker.AllowRequest(nowMs: 4_000));

        // After breakDurationMs (300 + 5000 = 5300ms), transitions to HalfOpen and allows probe
        Assert.IsTrue(breaker.AllowRequest(nowMs: 5_500));
        Assert.AreEqual(CircuitState.HalfOpen, breaker.State);

        breaker.RecordResult(succeeded: true, nowMs: 5_510);
        Assert.AreEqual(CircuitState.Closed, breaker.State);
    }
}

file sealed class TokenBucketLimiter
{
    private readonly double _capacity;
    private readonly double _refillRate;
    private double _tokens;
    private double _lastRefillSeconds;

    public TokenBucketLimiter(double capacity, double refillTokensPerSecond, double initialTimeSeconds)
    {
        _capacity = capacity;
        _refillRate = refillTokensPerSecond;
        _tokens = capacity;
        _lastRefillSeconds = initialTimeSeconds;
    }

    public bool TryAcquire(double cost, double nowSeconds)
    {
        if (nowSeconds > _lastRefillSeconds)
        {
            double elapsed = nowSeconds - _lastRefillSeconds;
            _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillRate));
            _lastRefillSeconds = nowSeconds;
        }

        if (_tokens >= cost)
        {
            _tokens -= cost;
            return true;
        }

        return false;
    }
}

file sealed class SlidingWindowCounterLimiter
{
    private readonly long _windowSeconds;
    private readonly int _limit;
    private long _currentWindowIndex;
    private int _previousCount;
    private int _currentCount;

    public SlidingWindowCounterLimiter(long windowSeconds, int limit)
    {
        _windowSeconds = windowSeconds;
        _limit = limit;
    }

    public bool TryAcquire(double nowSeconds)
    {
        long windowIdx = (long)(nowSeconds / _windowSeconds);
        if (windowIdx > _currentWindowIndex)
        {
            _previousCount = (windowIdx == _currentWindowIndex + 1) ? _currentCount : 0;
            _currentCount = 0;
            _currentWindowIndex = windowIdx;
        }

        double elapsedInCurrent = nowSeconds - (windowIdx * _windowSeconds);
        double fraction = elapsedInCurrent / _windowSeconds;
        double estimatedCount = (_previousCount * (1.0 - fraction)) + _currentCount;

        if (estimatedCount + 1.0 <= _limit + 1e-9)
        {
            _currentCount++;
            return true;
        }

        return false;
    }
}

file enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

file sealed class CircuitBreakerStateMachine
{
    private readonly int _failureThreshold;
    private readonly long _breakDurationMs;
    private int _consecutiveFailures;
    private long _openedAtMs;

    public CircuitState State { get; private set; } = CircuitState.Closed;

    public CircuitBreakerStateMachine(int failureThreshold, long breakDurationMs)
    {
        _failureThreshold = failureThreshold;
        _breakDurationMs = breakDurationMs;
    }

    public bool AllowRequest(long nowMs)
    {
        if (State == CircuitState.Closed)
        {
            return true;
        }

        if (State == CircuitState.Open && nowMs - _openedAtMs >= _breakDurationMs)
        {
            State = CircuitState.HalfOpen;
            return true;
        }

        return State == CircuitState.HalfOpen;
    }

    public void RecordResult(bool succeeded, long nowMs)
    {
        if (succeeded)
        {
            _consecutiveFailures = 0;
            State = CircuitState.Closed;
            return;
        }

        _consecutiveFailures++;
        if (State == CircuitState.HalfOpen || _consecutiveFailures >= _failureThreshold)
        {
            State = CircuitState.Open;
            _openedAtMs = nowMs;
        }
    }
}
