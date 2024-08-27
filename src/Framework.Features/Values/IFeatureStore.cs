namespace Framework.Features.Values;

public interface IFeatureStore
{
    Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey);
}

public sealed class NullFeatureStore : IFeatureStore
{
    public Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey)
    {
        return Task.FromResult<string?>(null);
    }
}
