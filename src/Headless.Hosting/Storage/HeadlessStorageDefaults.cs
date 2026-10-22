// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Storage defaults shared by every Headless relational feature, so an application that registers one connection
/// gets every feature's tables side by side in one schema instead of one schema per feature.
/// </summary>
/// <remarks>
/// Each feature still owns its own schema setting and reads this value only as that setting's default, so
/// overriding one feature's schema never moves another's.
/// </remarks>
[PublicAPI]
public static class HeadlessStorageDefaults
{
    /// <summary>The schema every Headless relational feature creates its objects in when none is configured.</summary>
    public const string Schema = "headless";
}
