namespace Liuvis.Web.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private static readonly Dictionary<string, RateLimitEntry> _counters = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed = await CheckRateLimitAsync(key);

        if (!allowed)
        {
            context.Response.StatusCode = 429;
            context.Response.ContentType = "application/json";
            var response = System.Text.Json.JsonSerializer.Serialize(new
            {
                code = 429,
                data = (object?)null,
                message = "Rate limit exceeded. Please try again later."
            });
            await context.Response.WriteAsync(response);
            return;
        }

        await _next(context);
    }

    private async Task<bool> CheckRateLimitAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            if (_counters.TryGetValue(key, out var entry))
            {
                if (now - entry.WindowStart > TimeSpan.FromMinutes(1))
                {
                    entry.WindowStart = now;
                    entry.Count = 1;
                    return true;
                }
                if (entry.Count >= 100)
                {
                    return false;
                }
                entry.Count++;
                return true;
            }
            _counters[key] = new RateLimitEntry { WindowStart = now, Count = 1 };
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private class RateLimitEntry
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }
}
