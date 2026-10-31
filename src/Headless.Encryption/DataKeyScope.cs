// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Encryption;

internal static class DataKeyScope
{
    /// <summary>Checks that <paramref name="scope" /> fits the scope column and compares the same on every store.</summary>
    public static string Validate(string? scope, string paramName)
    {
        Argument.IsNotNull(scope, paramName: paramName);
        Argument.HasMaxLength(scope, DataKeyFieldLimits.ScopeMaxLength, paramName: paramName);
        Argument.IsPortableKey(scope, paramName: paramName);

        return scope;
    }
}
