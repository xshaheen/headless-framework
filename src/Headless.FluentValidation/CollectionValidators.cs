// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace FluentValidation;

/// <summary>FluentValidation extension rules for <see cref="IEnumerable{T}"/> collection properties.</summary>
[PublicAPI]
public static class CollectionValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T, TElement>(IRuleBuilder<T, IEnumerable<TElement>> builder)
    {
        /// <summary>Validates that the collection contains at most <paramref name="maxElements"/> elements.</summary>
        /// <param name="maxElements">The maximum allowed number of elements. Must be positive.</param>
        /// <returns>The rule builder options for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxElements"/> is not positive.</exception>
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

        /// <summary>Validates that the collection contains at least <paramref name="minElements"/> elements.</summary>
        /// <param name="minElements">The minimum required number of elements. Must be zero or positive.</param>
        /// <returns>The rule builder options for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minElements"/> is negative.</exception>
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

        /// <summary>Validates that all elements in the collection are distinct.</summary>
        /// <param name="comparer">An optional equality comparer. Uses the default comparer when <see langword="null"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
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

        /// <summary>
        /// Validates that all elements produce distinct keys according to <paramref name="keySelector"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the key extracted from each element.</typeparam>
        /// <param name="keySelector">A function that extracts the comparison key from each element.</param>
        /// <param name="comparer">An optional equality comparer for keys. Uses the default comparer when <see langword="null"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
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

                        // Single pass: count excess items (each repeated key adds one), matching the
                        // non-keyed overload. Avoids GroupBy's Lookup + IGrouping allocations.
                        var seen = new HashSet<TKey>(comparer);
                        var duplicatesCount = 0;

                        foreach (var element in list)
                        {
                            if (!seen.Add(keySelector(element)))
                            {
                                duplicatesCount++;
                            }
                        }

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
