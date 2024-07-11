using FluentValidation;

namespace Framework.FluentValidation;

public static class PaginationValidators
{
    public static IRuleBuilder<T, int> PageIndex<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.GreaterThanOrEqualTo(0);
    }

    public static IRuleBuilder<T, int?> PageIndex<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThanOrEqualTo(0);
    }

    public static IRuleBuilder<T, int> PageSize<T>(this IRuleBuilder<T, int> rule, int maximumSize = 100)
    {
        return rule.GreaterThan(0).LessThanOrEqualTo(maximumSize);
    }

    public static IRuleBuilder<T, string?> SearchQuery<T>(this IRuleBuilder<T, string?> rule, int maximumLength = 100)
    {
        return rule.MaximumLength(maximumLength);
    }
}
