// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace FluentValidation;

[PublicAPI]
public static class IdValidators
{
    public static IRuleBuilderOptions<T, Guid> Id<T>(this IRuleBuilder<T, Guid> rule)
    {
        return rule.NotEqual(Guid.Empty);
    }

    public static IRuleBuilderOptions<T, Guid?> Id<T>(this IRuleBuilder<T, Guid?> rule)
    {
        return rule.NotEqual(Guid.Empty);
    }

    public static IRuleBuilderOptions<T, string?> Id<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.NotEqual(string.Empty);
    }

    public static IRuleBuilderOptions<T, int> Id<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, int?> Id<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, long> Id<T>(this IRuleBuilder<T, long> rule)
    {
        return rule.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, long?> Id<T>(this IRuleBuilder<T, long?> rule)
    {
        return rule.GreaterThan(0);
    }
}
