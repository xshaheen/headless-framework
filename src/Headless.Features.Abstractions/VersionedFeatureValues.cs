// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// The values of a group of features in one provider scope, with the version a later
/// <see cref="IFeatureManager.SetVersionedAsync"/> of the same group must present.
/// </summary>
/// <param name="Values">
/// One entry per requested feature. <see cref="FeatureValue.Provider"/> names the provider that supplied the value; a
/// <see langword="null"/> <see cref="FeatureValue.Value"/> means no provider holds one.
/// </param>
/// <param name="Version">
/// An opaque token of the current stored representation at this scope. An unchanged save preserves it, and returning
/// to an absent representation can reuse it. Inherited values are not covered. Use it as a scope-local HTTP entity tag.
/// </param>
[PublicAPI]
public sealed record VersionedFeatureValues(IReadOnlyList<FeatureValue> Values, string Version);
