// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using MoreLinq.Extensions;

namespace FluentValidation;

[PublicAPI]
public static class CollectionValidators
{
    extension<T, TElement>(IRuleBuilder<T, IEnumerable<TElement>?> builder)
    {
        public IRuleBuilderOptions<T, IEnumerable<TElement>?> MaximumElements(int maxElements)
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

        public IRuleBuilderOptions<T, IEnumerable<TElement>?> MinimumElements(int minElements)
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

        public IRuleBuilderOptions<T, IEnumerable<TElement>?> UniqueElements(
            IEqualityComparer<TElement>? comparer = null
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

                        // ReSharper disable once PossibleMultipleEnumeration
                        var count = list.TryGetNonEnumeratedCount(out var length) ? length : list.Count();

                        var hashSet = new HashSet<TElement>(count, comparer);
                        // ReSharper disable once PossibleMultipleEnumeration
                        var hasDuplicates = list.Any(element => !hashSet.Add(element));

                        if (!hasDuplicates)
                        {
                            return true;
                        }

                        context.MessageFormatter.AppendArgument("TotalDuplicates", count - hashSet.Count);

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.UniqueElementsValidator());
        }

        public IRuleBuilderOptions<T, IEnumerable<TElement>?> UniqueElements<TKey>(
            Func<TElement, TKey> keySelector,
            IEqualityComparer<TKey>? comparer = null
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

                        var duplicatesCount = list.GroupBy(keySelector, comparer)
                            .Count(elements => elements.AtLeast(2));

                        if (duplicatesCount == 0)
                        {
                            return true;
                        }

                        context.MessageFormatter.AppendArgument("TotalDuplicates", duplicatesCount);

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.UniqueElementsValidator());
        }
    }
}
