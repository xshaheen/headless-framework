// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using Headless.Checks;

namespace FluentValidation;

[PublicAPI]
public static class CollectionValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T, TElement>(IRuleBuilder<T, IEnumerable<TElement>> builder)
    {
        public IRuleBuilderOptions<T, IEnumerable<TElement>> MaximumElements(int maxElements)
        {
            Argument.IsPositive(maxElements);

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

        public IRuleBuilderOptions<T, IEnumerable<TElement>> MinimumElements(int minElements)
        {
            Argument.IsPositiveOrZero(minElements);

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

        public IRuleBuilderOptions<T, IEnumerable<TElement>> UniqueElements(IEqualityComparer<TElement> comparer = null)
        {
            return builder
                .Must(
                    (_, list, context) =>
                    {
                        if (list is null)
                        {
                            return true;
                        }

                        // Single pass: HashSet.Add returns false for each duplicate, so the failed-add
                        // count is the exact number of excess items. (Any() would short-circuit on the
                        // first duplicate, leaving the set partially filled and the count wrong.)
                        var hashSet = new HashSet<TElement>(comparer);
                        var duplicates = 0;

                        foreach (var element in list)
                        {
                            if (!hashSet.Add(element))
                            {
                                duplicates++;
                            }
                        }

                        if (duplicates == 0)
                        {
                            return true;
                        }

                        context.MessageFormatter.AppendArgument("TotalDuplicates", duplicates);

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Collections.UniqueElementsValidator());
        }

        public IRuleBuilderOptions<T, IEnumerable<TElement>> UniqueElements<TKey>(
            Func<TElement, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null
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

                        // Count excess items (matching the non-keyed overload's `count - distinct`),
                        // not the number of keys that have duplicates.
                        var duplicatesCount = list.GroupBy(keySelector, comparer).Sum(elements => elements.Count() - 1);

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
#nullable restore
}
