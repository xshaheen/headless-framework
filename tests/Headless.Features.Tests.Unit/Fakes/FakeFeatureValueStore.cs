// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;

namespace Tests.Fakes;

/// <summary>An in-memory <see cref="IFeatureValueStore"/> keyed by name, provider name, and provider key.</summary>
internal sealed class FakeFeatureValueStore : IFeatureValueStore
{
    private readonly Dictionary<(string Name, string ProviderName, string? ProviderKey), string?> _values = [];

    public IReadOnlyCollection<(string Name, string ProviderName, string? ProviderKey)> Keys => _values.Keys;

    public void Seed(string name, string providerName, string? providerKey, string value)
    {
        _values[(name, providerName, providerKey)] = value;
    }

    public string? Find(string name, string providerName, string? providerKey)
    {
        return _values.GetValueOrDefault((name, providerName, providerKey));
    }

    public Task<string?> GetOrDefaultAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(_values.GetValueOrDefault((name, providerName, providerKey)));
    }

    public Task<List<FeatureValue>> GetAllAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var values = names
            .Select(name => new FeatureValue(name, _values.GetValueOrDefault((name, providerName, providerKey))))
            .ToList();

        return Task.FromResult(values);
    }

    public Task SetAsync(
        string name,
        string value,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _values[(name, providerName, providerKey)] = value;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _values.Remove((name, providerName, providerKey));

        return Task.CompletedTask;
    }

    public Task SetAllAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var (name, value) in values)
        {
            if (value is null)
            {
                _values.Remove((name, providerName, providerKey));
            }
            else
            {
                _values[(name, providerName, providerKey)] = value;
            }
        }

        return Task.CompletedTask;
    }
}
