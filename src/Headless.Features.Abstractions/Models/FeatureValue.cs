// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Models;

/// <summary>Holds the resolved value of a feature along with the provider that supplied it.</summary>
/// <param name="Name">The feature name.</param>
/// <param name="Value">The feature's current string value, or <see langword="null"/> if no value is set.</param>
/// <param name="Provider">The value provider that resolved the value, or <see langword="null"/> if the value came from the feature's default.</param>
public sealed record FeatureValue(string Name, string? Value, FeatureValueProvider? Provider);

/// <summary>Identifies the value provider that supplied a feature value.</summary>
/// <param name="Name">The provider name (e.g., <c>Tenant</c>, <c>Edition</c>, <c>DefaultValue</c>).</param>
/// <param name="Key">The provider-specific key (e.g., a tenant ID or edition ID), or <see langword="null"/> when not applicable.</param>
public sealed record FeatureValueProvider(string Name, string? Key);
