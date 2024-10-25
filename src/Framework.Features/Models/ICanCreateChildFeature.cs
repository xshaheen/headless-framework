// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Models;

public interface ICanCreateChildFeature
{
    FeatureDefinition AddChild(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true
    );
}
