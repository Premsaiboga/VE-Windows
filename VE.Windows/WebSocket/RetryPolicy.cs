namespace VE.Windows.WebSocket;

/// <summary>
/// Retry policy with exponential backoff for WebSocket connections.
/// Equivalent to macOS RetryPolicy.
/// </summary>
public class RetryPolicy
{
    public int MaxRetries { get; init; } = -1; // -1 = unlimited
    public double InitialDelay { get; init; } = 1.0;
    public double MaxDelay { get; init; } = 60.0;
    public double BackoffMultiplier { get; init; } = 2.0;
    public double JitterFactor { get; init; } = 0.1;

    public static RetryPolicy Default => new()
    {
        MaxRetries = -1,
        InitialDelay = 1.0,
        MaxDelay = 60.0,
        BackoffMultiplier = 2.0,
        JitterFactor = 0.1
    };

    public TimeSpan CalculateDelay(int attempt)
    {
        var exponentialDelay = InitialDelay * Math.Pow(BackoffMultiplier, attempt);
        var cappedDelay = Math.Min(exponentialDelay, MaxDelay);
        var jitter = cappedDelay * JitterFactor * (Random.Shared.NextDouble() * 2 - 1);
        var finalDelay = Math.Max(0, cappedDelay + jitter);
        return TimeSpan.FromSeconds(finalDelay);
    }

    public bool ShouldRetry(int attempt)
    {
        return MaxRetries < 0 || attempt < MaxRetries;
    }
}
