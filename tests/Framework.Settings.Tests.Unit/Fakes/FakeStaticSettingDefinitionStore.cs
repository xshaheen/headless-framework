// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Definitions;
using Framework.Settings.Models;

namespace Tests.Fakes;

public sealed class FakeStaticSettingDefinitionStore : IStaticSettingDefinitionStore
{
    private readonly Dictionary<string, SettingDefinition> _definitions = new(StringComparer.Ordinal);

    public void Add(SettingDefinition definition) => _definitions[definition.Name] = definition;

    public void AddRange(params SettingDefinition[] definitions)
    {
        foreach (var definition in definitions)
        {
            Add(definition);
        }
    }

    public void Clear() => _definitions.Clear();

    public Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_definitions.GetValueOrDefault(name));

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SettingDefinition>>(_definitions.Values.ToList());
}
