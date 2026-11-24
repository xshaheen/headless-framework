// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Builds the trace page's tree from every module's trace items and flattens it into rows with a depth, so the page
/// draws a tree without a tree library and every placement rule is tested here.
/// </summary>
internal static class TraceTreeBuilder
{
    /// <summary>A message published inside a consumer whose generation cannot be told apart, placed under the message.</summary>
    internal const string ConsumerNotRecordedTag = "consumer-not-recorded";

    /// <summary>A child whose cause is not on the page: it was limited, stale, unavailable, or hidden.</summary>
    internal const string ParentNotOnPageTag = "parent-not-on-page";

    /// <summary>An item whose causes loop back to itself, drawn as a root where the loop was broken.</summary>
    internal const string CycleTag = "cycle";

    /// <summary>A child stamped earlier than its parent; it keeps its place under the parent.</summary>
    internal const string ClockSkewTag = "clock-skew";

    /// <summary>
    /// Places each item under its cause and returns the rows depth first, siblings and roots ordered by time. An item
    /// that repeats another's module, store, and storage id is dropped.
    /// </summary>
    internal static IReadOnlyList<TraceRow> Build(IReadOnlyList<TraceEntry> entries)
    {
        var nodes = _Dedupe(entries);
        var byKey = new Dictionary<(DashboardTraceKeyKind, string), Node>();
        var generationsByMessage = new Dictionary<string, List<Node>>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            byKey.TryAdd((node.Item.Key.Kind, node.Item.Key.Value), node);

            if (node.Item is { Key.Kind: DashboardTraceKeyKind.Delivery, DeliversMessageId: { } messageId })
            {
                if (!generationsByMessage.TryGetValue(messageId, out var generations))
                {
                    generationsByMessage[messageId] = generations = [];
                }

                generations.Add(node);
            }
        }

        foreach (var node in nodes)
        {
            _Attach(node, byKey, generationsByMessage);
        }

        _BreakCycles(nodes);

        return _Flatten(nodes);
    }

    private static List<Node> _Dedupe(IReadOnlyList<TraceEntry> entries)
    {
        var seen = new HashSet<(string, string, string)>();
        var nodes = new List<Node>(entries.Count);

        foreach (var entry in entries)
        {
            // Each module already dedupes before its limit; this guards the tree against a module that did not.
            if (seen.Add((entry.ModuleId, entry.Item.Store, entry.Item.StorageId)))
            {
                nodes.Add(new Node(entry.ModuleId, entry.Item, nodes.Count));
            }
        }

        return nodes;
    }

    private static void _Attach(
        Node node,
        Dictionary<(DashboardTraceKeyKind, string), Node> byKey,
        Dictionary<string, List<Node>> generationsByMessage
    )
    {
        var item = node.Item;

        // A received generation copies the envelope's causation, which names the publisher's parent, so it hangs under
        // the published message it delivers instead.
        if (item is { Key.Kind: DashboardTraceKeyKind.Delivery, DeliversMessageId: { } messageId })
        {
            _SetParentOrOrphan(node, byKey.GetValueOrDefault((DashboardTraceKeyKind.Message, messageId)));

            return;
        }

        if (item.Causation is not { } causation)
        {
            return;
        }

        if (causation.Kind == DashboardTraceKeyKind.Message)
        {
            // A publish inside a consumer records only the consumed message id, so the generation that ran it is known
            // only when exactly one generation of that message is on the page.
            if (generationsByMessage.TryGetValue(causation.Value, out var generations) && generations.Count == 1)
            {
                _SetParentOrOrphan(node, generations[0]);

                return;
            }

            var published = byKey.GetValueOrDefault((DashboardTraceKeyKind.Message, causation.Value));
            _SetParentOrOrphan(node, published);

            if (published is not null)
            {
                node.Tags.Add(ConsumerNotRecordedTag);
            }

            return;
        }

        _SetParentOrOrphan(node, byKey.GetValueOrDefault((causation.Kind, causation.Value)));
    }

    private static void _SetParentOrOrphan(Node node, Node? parent)
    {
        if (parent is null)
        {
            // The tag never says why the parent is missing: naming a hidden module would describe what the user may
            // not see. The module sections already say which module was limited, stale, unavailable, or hidden.
            node.Tags.Add(ParentNotOnPageTag);

            return;
        }

        node.Parent = parent;
    }

    // Walks each item's chain of causes in time order; a chain that comes back to an item still being walked is a
    // loop, broken at that repeated item, which becomes a root.
    private static void _BreakCycles(List<Node> nodes)
    {
        var state = new Dictionary<Node, int>();

        foreach (var start in nodes.Order(NodeOrder.Instance))
        {
            var path = new List<Node>();
            var current = start;

            while (current is not null && state.GetValueOrDefault(current) == 0)
            {
                state[current] = 1;
                path.Add(current);
                current = current.Parent;
            }

            if (current is not null && state[current] == 1)
            {
                current.Parent = null;
                current.Tags.Add(CycleTag);
            }

            foreach (var walked in path)
            {
                state[walked] = 2;
            }
        }
    }

    private static List<TraceRow> _Flatten(List<Node> nodes)
    {
        var children = new Dictionary<Node, List<Node>>();
        var roots = new List<Node>();

        foreach (var node in nodes)
        {
            if (node.Parent is null)
            {
                roots.Add(node);

                continue;
            }

            if (node.Item.At is { } at && node.Parent.Item.At is { } parentAt && at < parentAt)
            {
                node.Tags.Add(ClockSkewTag);
            }

            if (!children.TryGetValue(node.Parent, out var siblings))
            {
                children[node.Parent] = siblings = [];
            }

            siblings.Add(node);
        }

        var rows = new List<TraceRow>(nodes.Count);
        var stack = new Stack<(Node Node, int Depth)>();

        // Pushed in reverse so the earliest pops first; an explicit stack keeps a long chain of runs off the call stack.
        foreach (var root in roots.OrderDescending(NodeOrder.Instance))
        {
            stack.Push((root, 0));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            rows.Add(new TraceRow(node.ModuleId, node.Item, depth, node.Tags));

            if (children.TryGetValue(node, out var siblings))
            {
                foreach (var child in siblings.OrderDescending(NodeOrder.Instance))
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        return rows;
    }

    private sealed class Node(string moduleId, DashboardTraceItem item, int index)
    {
        public string ModuleId { get; } = moduleId;

        public DashboardTraceItem Item { get; } = item;

        public int Index { get; } = index;

        public Node? Parent { get; set; }

        public List<string> Tags { get; } = [];
    }

    // Time first, and an item with no time yet after every timed one, as a store orders an unscheduled run; two items
    // at the same instant, or both without one, keep the order the modules answered in.
    private sealed class NodeOrder : IComparer<Node>
    {
        public static readonly NodeOrder Instance = new();

        public int Compare(Node? x, Node? y)
        {
            var byTime = (x!.Item.At, y!.Item.At) switch
            {
                ({ } a, { } b) => a.CompareTo(b),
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
            };

            return byTime != 0 ? byTime : x.Index.CompareTo(y.Index);
        }
    }
}

/// <summary>One module's trace item, as the tree builder takes it.</summary>
internal sealed record TraceEntry(string ModuleId, DashboardTraceItem Item);

/// <summary>One row of the flattened tree: the item, how deep it sits, and the tags that explain its place.</summary>
internal sealed record TraceRow(string ModuleId, DashboardTraceItem Item, int Depth, IReadOnlyList<string> Tags);
