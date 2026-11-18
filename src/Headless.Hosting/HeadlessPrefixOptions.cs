// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Hosting;

/// <summary>
/// The isolation prefix this host puts in front of every name it writes to a shared store, so several environments
/// (worktrees, preview stacks, sandboxes) can share one Redis or one broker without reading, deleting, or locking each
/// other's data.
/// </summary>
/// <remarks>
/// <para>
/// Set it once for the whole host: in configuration under <see cref="ConfigurationKey" /> (environment variable
/// <c>Headless__Prefix</c>), or in code with <c>services.ConfigureHeadlessPrefix(...)</c>, which wins over
/// configuration. Every Headless feature that writes shared names reads it; no feature needs its own setting.
/// </para>
/// <para>
/// Empty, the default, means no prefix: every name stays exactly as it is without one. The value is a naming
/// boundary, not a security boundary; credentials still decide who can reach a store.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessPrefixOptions
{
    /// <summary>The configuration key the prefix is read from. Value: <c>"Headless:Prefix"</c>.</summary>
    public const string ConfigurationKey = "Headless:Prefix";

    /// <summary>
    /// The longest prefix accepted. It keeps every prefixed broker name inside the tightest name limit Headless
    /// writes to.
    /// </summary>
    public const int MaxLength = 20;

    /// <summary>
    /// The prefix: empty, or 1 to <see cref="MaxLength" /> lowercase letters, digits, and <c>-</c>, neither starting
    /// nor ending with <c>-</c>. Defaults to empty.
    /// </summary>
    /// <remarks>
    /// The character set holds no separator a store uses (<c>:</c>, <c>.</c>, <c>_</c>, <c>/</c>) and no pattern
    /// character (<c>*</c>, <c>?</c>, <c>[</c>), so a prefix cannot reach into another environment's names through a
    /// separator or widen a pattern-based delete.
    /// </remarks>
    public string Prefix { get; set; } = "";

    /// <summary>Returns <paramref name="name" /> with the prefix and <paramref name="separator" /> in front of it.</summary>
    /// <param name="name">The name to prefix.</param>
    /// <param name="separator">The text between the prefix and the name, such as <c>":"</c>.</param>
    /// <returns><paramref name="name" /> unchanged when the prefix is empty; otherwise the prefixed name.</returns>
    public string Apply(string name, string separator)
    {
        return Prefix.Length == 0 ? name : string.Concat(Prefix, separator, name);
    }

    internal static bool HasValidShape(string prefix)
    {
        if (prefix.Length == 0)
        {
            return true;
        }

        if (prefix[0] == '-' || prefix[^1] == '-')
        {
            return false;
        }

        foreach (var c in prefix)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class HeadlessPrefixOptionsValidator : AbstractValidator<HeadlessPrefixOptions>
{
    public HeadlessPrefixOptionsValidator()
    {
        RuleFor(x => x.Prefix)
            .NotNull()
            .MaximumLength(HeadlessPrefixOptions.MaxLength)
            .WithMessage(
                $"{HeadlessPrefixOptions.ConfigurationKey} must be at most {HeadlessPrefixOptions.MaxLength} characters."
            )
            .Must(HeadlessPrefixOptions.HasValidShape)
            .WithMessage(
                $"{HeadlessPrefixOptions.ConfigurationKey} must use only lowercase letters, digits, and '-', and must not start or end with '-'."
            );
    }
}
