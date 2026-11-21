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
/// An opaque token that changes on every save of any setting in the group at this scope, even one that stores the
/// same value. Use it as an HTTP entity tag.
/// </param>
[PublicAPI]
public sealed record VersionedSettingValues(IReadOnlyList<SettingValue> Values, string Version);
