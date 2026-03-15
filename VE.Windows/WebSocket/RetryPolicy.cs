namespace VE.Windows.WebSocket;

/// <summary>
/// Retry policy with exponential backoff + jitter for WebSocket reconnection.
/// Matches macOS RetryPolicy: unlimited retries, 1s initial delay, 60s max, 2x multiplier,
/// jitter to prevent thundering herd.
/// </summary>
public class RetryPolicy
{
    public int MaxRetries { get; init; } = -1; // -1 = unlimited
    public double InitialDelay { get; init; } = 1.0;
    public double MaxDelay { get; init; } = 60.0;
    public double BackoffMultiplier { get; init; } = 2.0;
    public double JitterFactor { get; init; } = 0.3;

    private int _attempt;

    public static RetryPolicy Default => new()
    {
        MaxRetries = -1,
        InitialDelay = 1.0,
        MaxDelay = 60.0,
        BackoffMultiplier = 2.0,
        JitterFactor = 0.3
    };

    /// <summary>
    /// Calculate next delay with exponential backoff and jitter.
    /// Formula: min(initialDelay * multiplier^attempt, maxDelay) + random jitter.
    /// </summary>
    public TimeSpan CalculateDelay(int attempt)
    {
        var baseDelay = Math.Min(InitialDelay * Math.Pow(BackoffMultiplier, attempt), MaxDelay);
        var jitter = baseDelay * JitterFactor * Random.Shared.NextDouble();
        _attempt = attempt + 1;
        return TimeSpan.FromSeconds(baseDelay + jitter);
    }

    /// <summary>
    /// Calculate next delay using internal attempt counter.
    /// </summary>
    public TimeSpan NextDelay()
    {
        var delay = CalculateDelay(_attempt);
        return delay;
    }

    public bool ShouldRetry(int attempt)
    {
        return MaxRetries < 0 || attempt < MaxRetries;
    }

    /// <summary>
    /// Reset attempt counter (call on successful connection).
    /// </summary>
    public void Reset() => _attempt = 0;
}
