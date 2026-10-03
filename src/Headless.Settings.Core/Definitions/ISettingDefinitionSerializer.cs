// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Primitives;
using Headless.Settings.Entities;
using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

/// <summary>Converts between <see cref="SettingDefinition"/> domain objects and <see cref="SettingDefinitionRecord"/> persistence entities.</summary>
public interface ISettingDefinitionSerializer
{
    /// <summary>Converts a single <see cref="SettingDefinition"/> to its corresponding <see cref="SettingDefinitionRecord"/>.</summary>
    /// <param name="setting">The setting definition to serialize.</param>
    /// <returns>A new <see cref="SettingDefinitionRecord"/> representing <paramref name="setting"/>.</returns>
    SettingDefinitionRecord Serialize(SettingDefinition setting);

    /// <summary>Converts a collection of <see cref="SettingDefinition"/> objects to their corresponding <see cref="SettingDefinitionRecord"/> list.</summary>
    /// <param name="settings">The setting definitions to serialize.</param>
    /// <returns>A list of <see cref="SettingDefinitionRecord"/> instances, one per input definition.</returns>
    List<SettingDefinitionRecord> Serialize(IEnumerable<SettingDefinition> settings);

    /// <summary>Reconstructs a <see cref="SettingDefinition"/> from its persisted <see cref="SettingDefinitionRecord"/>.</summary>
    /// <param name="record">The record to deserialize.</param>
    /// <returns>The <see cref="SettingDefinition"/> represented by <paramref name="record"/>.</returns>
    SettingDefinition Deserialize(SettingDefinitionRecord record);

    /// <summary>Reconstructs a list of <see cref="SettingDefinition"/> objects from their persisted <see cref="SettingDefinitionRecord"/> instances.</summary>
    /// <param name="records">The records to deserialize.</param>
    /// <returns>A list of <see cref="SettingDefinition"/> instances, one per input record.</returns>
    List<SettingDefinition> Deserialize(IEnumerable<SettingDefinitionRecord> records);
}
