// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>Storage-independent identity rules for durable Jobs contracts.</summary>
[PublicAPI]
public static class JobContract
{
    /// <summary>Maximum function-name length in UTF-16 code units.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Maximum schema-version length in UTF-16 code units.</summary>
    public const int VersionMaxLength = 100;

    /// <summary>The schema version assigned to legacy jobs by the consumer's migration.</summary>
    public const string LegacyVersion = "1";

    internal static string ValidateName(string value) => _Validate(value, NameMaxLength, nameof(value));

    internal static string ValidateVersion(string value) => _Validate(value, VersionMaxLength, nameof(value));

    private static string _Validate(string value, int maximumLength, string parameterName)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
        )
        {
            throw new ArgumentException(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Job contract identities must contain 1 to {maximumLength} characters without surrounding whitespace."
                ),
                parameterName
            );
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (
                char.IsControl(value[i])
                || (
                    char.IsSurrogate(value[i])
                    && (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i]))
                )
            )
            {
                throw new ArgumentException(
                    "Job contract identities cannot contain control characters or invalid Unicode.",
                    parameterName
                );
            }
        }

        return value;
    }
}
