using FluentValidation;
using Framework.FluentValidation.Resources;

namespace Framework.FluentValidation;

public static class CollectionValidators
{
    public static IRuleBuilderOptions<T, IEnumerable<TElement>?> MaximumElements<T, TElement>(
        this IRuleBuilder<T, IEnumerable<TElement>?> builder,
        int maxElements
    )
    {
        return builder
            .Must(
                (_, list, context) =>
                {
                    if (list is null)
                    {
                        return true;
                    }

                    if (!list.TryGetNonEnumeratedCount(out var length))
                    {
                        length = list.Count();
                    }

                    if (length <= maxElements)
                    {
                        return true;
                    }

                    context
                        .MessageFormatter.AppendArgument("MaxElements", maxElements)
                        .AppendArgument("TotalElements", length);

                    return false;
                }
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.MaximumElementsValidator());
    }

    public static IRuleBuilderOptions<T, IEnumerable<TElement>?> MinimumElements<T, TElement>(
        this IRuleBuilder<T, IEnumerable<TElement>?> builder,
        int minElements
    )
    {
        return builder
            .Must(
                (_, list, context) =>
                {
                    if (list is null)
                    {
                        return true;
                    }

                    if (!list.TryGetNonEnumeratedCount(out var length))
                    {
                        length = list.Count();
                    }

                    if (length >= minElements)
                    {
                        return true;
                    }

                    context
                        .MessageFormatter.AppendArgument("MinElements", minElements)
                        .AppendArgument("TotalElements", length);

                    return false;
                }
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.MinimumElementsValidator());
    }

    public static IRuleBuilderOptions<T, IEnumerable<TElement>?> UniqueElements<T, TElement, TKey>(
        this IRuleBuilder<T, IEnumerable<TElement>?> builder,
        Func<TElement, TKey> keySelector
    )
    {
        return builder
            .Must(
                (_, list, context) =>
                {
                    if (list is null)
                    {
                        return true;
                    }

                    var duplicates = list.GroupBy(keySelector)
                        .Where(elements => elements.Skip(1).Any())
                        .Select(elements => elements.Key)
                        .ToList();

                    context.MessageFormatter.AppendArgument("TotalDuplicates", duplicates.Count);

                    return duplicates.Count == 0;
                }
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.UniqueElementsValidator());
    }
}
