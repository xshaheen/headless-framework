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
/// An opaque token that changes on every save of any feature in the group at this scope, even one that stores the
/// same value. Use it as an HTTP entity tag.
/// </param>
[PublicAPI]
public sealed record VersionedFeatureValues(IReadOnlyList<FeatureValue> Values, string Version);
