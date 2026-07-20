// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>FluentValidation extension rules for common pagination and search parameters.</summary>
[PublicAPI]
public static class HeadlessPaginationValidators
{
    /// <summary>Validates that the nullable page index, when present, is zero or greater.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, int?> PageIndex<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThanOrEqualTo(0);
    }

#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>
    /// Validates that the search query does not exceed <paramref name="maximumLength"/> characters.
    /// </summary>
    /// <param name="maximumLength">The maximum allowed character length. Defaults to 100. Must be zero or positive.</param>
    /// <returns>The rule builder options for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumLength"/> is negative.</exception>
    public static IRuleBuilderOptions<T, string> SearchQuery<T>(
        this IRuleBuilder<T, string> rule,
        int maximumLength = 100
    )
#nullable restore
    {
        Argument.IsPositiveOrZero(maximumLength);

        return rule.MaximumLength(maximumLength);
    }

    extension<T>(IRuleBuilder<T, int> rule)
    {
        /// <summary>Validates that the page index is zero or greater.</summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, int> PageIndex()
        {
            return rule.GreaterThanOrEqualTo(0);
        }

        /// <summary>Validates that the page size is between 1 and <paramref name="maximumSize"/> (inclusive).</summary>
        /// <param name="maximumSize">The maximum allowed page size. Defaults to 100. Must be positive.</param>
        /// <returns>The rule builder options for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumSize"/> is not positive.</exception>
        public IRuleBuilderOptions<T, int> PageSize(int maximumSize = 100)
        {
            Argument.IsPositive(maximumSize);

            return rule.GreaterThan(0).LessThanOrEqualTo(maximumSize);
        }
    }
}
