// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Features.Models;

public sealed record FeatureValue(string Name, string? Value, FeatureValueProvider? Provider);

public sealed record FeatureValueProvider(string Name, string? Key);
