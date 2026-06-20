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
    /// <summary>Production environment (<c>Production</c>).</summary>
    public const string Production = "Production";

    /// <summary>Staging (pre-production) environment (<c>Staging</c>).</summary>
    public const string Staging = "Staging";

    /// <summary>Automated-test environment (<c>Test</c>); a framework convention, not an ASP.NET Core built-in.</summary>
    public const string Test = "Test";

    /// <summary>Local development environment (<c>Development</c>).</summary>
    public const string Development = "Development";
}
