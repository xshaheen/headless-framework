// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace FluentValidation;

[PublicAPI]
public static class PaginationValidators
{
    public static IRuleBuilderOptions<T, int?> PageIndex<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThanOrEqualTo(0);
    }

    public static IRuleBuilderOptions<T, string?> SearchQuery<T>(
        this IRuleBuilder<T, string?> rule,
        int maximumLength = 100
    )
    {
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
            return rule.GreaterThan(0).LessThanOrEqualTo(maximumSize);
        }
    }
}
