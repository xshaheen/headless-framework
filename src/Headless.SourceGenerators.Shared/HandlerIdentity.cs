// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.SourceGenerators;

/// <summary>
/// Enforces handler identity syntax (<c>owner.name</c>) during compilation.
/// </summary>
/// <remarks>
/// The owner segment identifies the owning service or module for host routing and filters.
/// Identities cannot exceed <see cref="MaxLength"/> characters and must not contain control characters
/// or unpaired surrogates.
/// </remarks>
internal static class HandlerIdentity
{
    /// <summary>Maximum allowed length for a handler identity in characters.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Validates whether the specified identity adheres to the <c>owner.name</c> format.
    /// </summary>
    /// <param name="identity">The identity string to validate.</param>
    /// <returns><see langword="true"/> if the identity is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(string? identity)
    {
        if (
            identity is not { Length: > 0 and <= MaxLength }
            || char.IsWhiteSpace(identity[0])
            || char.IsWhiteSpace(identity[identity.Length - 1])
        )
        {
            return false;
        }

        var segmentLength = 0;
        var segments = 0;
        for (var i = 0; i < identity.Length; i++)
        {
            var c = identity[i];
            if (char.IsControl(c))
            {
                return false;
            }

            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 == identity.Length || !char.IsLowSurrogate(identity[i + 1]))
                {
                    return false;
                }

                i++;
                segmentLength++;
                continue;
            }

            if (c == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }

                segments++;
                segmentLength = 0;
                continue;
            }

            segmentLength++;
        }

        // A trailing segment is required, and at least one separator must precede it.
        return segmentLength > 0 && segments > 0;
    }
}
