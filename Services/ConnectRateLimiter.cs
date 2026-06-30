using Microsoft.Extensions.Caching.Memory;

namespace CompareD.Services;

// Per-user rate limiter for database Connect attempts (brute-force mitigation)
public class ConnectRateLimiter
{
    private readonly IMemoryCache _cache;
    private const int MaxAttempts = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public ConnectRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool IsAllowed(string userKey)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            userKey = "anonymous";
        }

        var cacheKey = $"connect_rate_{userKey}";
        var count = _cache.Get<int?>(cacheKey) ?? 0;

        if (count >= MaxAttempts)
        {
            return false;
        }

        _cache.Set(cacheKey, count + 1, Window);
        return true;
    }
}
