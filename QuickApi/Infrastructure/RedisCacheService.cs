using StackExchange.Redis;
using System.Text.Json;

namespace QuickApi.Infrastructure;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _mux;

    public RedisCacheService(IConnectionMultiplexer mux)
    {
        _mux = mux;
        _db = mux.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(key);
        if (val.IsNullOrEmpty) return default;
        return JsonSerializer.Deserialize<T>(val!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, ttl);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => _db.KeyDeleteAsync(key);

    
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var endpoints = _mux.GetEndPoints();
        foreach (var ep in endpoints)
        {
            var server = _mux.GetServer(ep);
            foreach (var key in server.Keys(pattern: $"{prefix}*"))
                await _db.KeyDeleteAsync(key);
        }
    }
}
