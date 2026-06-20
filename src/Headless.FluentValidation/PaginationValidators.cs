// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace FluentValidation;

[PublicAPI]
public static class PaginationValidators
{
    public static IRuleBuilderOptions<T, int?> PageIndex<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThanOrEqualTo(0);
    }

#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
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
        public IRuleBuilderOptions<T, int> PageIndex()
        {
            return rule.GreaterThanOrEqualTo(0);
        }

        public IRuleBuilderOptions<T, int> PageSize(int maximumSize = 100)
        {
            Argument.IsPositive(maximumSize);

            return rule.GreaterThan(0).LessThanOrEqualTo(maximumSize);
        }
    }
}
