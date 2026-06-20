// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Well-known hosting environment names. <see cref="Production"/>, <see cref="Staging"/>, and
/// <see cref="Development"/> match the conventional ASP.NET Core environment names
/// (<c>Environments.*</c>); <see cref="Test"/> is an additional framework convention for the test
/// environment. Compare against the current environment name case-insensitively.
/// </summary>
[PublicAPI]
public static class EnvironmentNames
{
    public const string Production = "Production";
    public const string Staging = "Staging";
    public const string Test = "Test";
    public const string Development = "Development";
}
