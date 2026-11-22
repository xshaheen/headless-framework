// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

/// <summary>
/// The values of a group of settings in one provider scope, with the version a later
/// <see cref="ISettingManager.SetVersionedAsync"/> of the same group must present.
/// </summary>
/// <param name="Values">
/// One entry per requested setting. <see cref="SettingValue.Provider"/> names the provider that supplied the value; a
/// <see langword="null"/> <see cref="SettingValue.Value"/> means no provider holds one.
/// </param>
/// <param name="Version">
/// An opaque token of the current stored representation at this scope. An unchanged save preserves it, and returning
/// to an absent representation can reuse it. Inherited values are not covered. Use it as a scope-local HTTP entity tag.
/// </param>
[PublicAPI]
public sealed record VersionedSettingValues(IReadOnlyList<SettingValue> Values, string Version);
