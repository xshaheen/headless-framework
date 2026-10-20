// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Orders items so each follows the items it depends on, keeping registration order otherwise.</summary>
internal static class DependencyOrder
{
    /// <summary>
    /// Returns <paramref name="items" /> ordered so every item follows the items <paramref name="dependenciesOf" />
    /// names. A dependency that is not in <paramref name="items" /> is ignored: it is not this host's to run.
    /// </summary>
    /// <param name="items">The items, in registration order.</param>
    /// <param name="dependenciesOf">Selects, from all items, the ones an item must follow.</param>
    /// <param name="describe">Names an item in the cycle message.</param>
    /// <param name="what">Names the items in the cycle message, such as <c>Resource provisioners</c>.</param>
    /// <exception cref="InvalidOperationException">Two items depend on each other.</exception>
    public static List<T> Sort<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<T>, IEnumerable<T>> dependenciesOf,
        Func<T, string> describe,
        string what
    )
        where T : class
    {
        var ordered = new List<T>(items.Count);
        var registered = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        var done = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var visiting = new List<T>();

        foreach (var item in items)
        {
            visit(item);
        }

        return ordered;

        void visit(T item)
        {
            if (done.Contains(item))
            {
                return;
            }

            if (visiting.Contains(item, ReferenceEqualityComparer.Instance))
            {
                var cycle = visiting.SkipWhile(x => !ReferenceEquals(x, item)).Append(item).Select(describe);

                throw new InvalidOperationException($"{what} depend on each other: {string.Join(" -> ", cycle)}.");
            }

            visiting.Add(item);

            foreach (var dependency in dependenciesOf(item, items).Where(registered.Contains))
            {
                visit(dependency);
            }

            visiting.RemoveAt(visiting.Count - 1);
            done.Add(item);
            ordered.Add(item);
        }
    }
}
