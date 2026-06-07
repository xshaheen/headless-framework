// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Incarnation-qualified node identity in the canonical <c>node@incarnation</c> form.</summary>
[PublicAPI]
public readonly record struct NodeIdentity
{
    private const char _Separator = '@';

    public NodeIdentity(NodeId nodeId, NodeIncarnation incarnation)
    {
        NodeId = nodeId;
        Incarnation = incarnation;
    }

    public NodeId NodeId { get; }

    public NodeIncarnation Incarnation { get; }

    public static NodeIdentity Parse(string value)
    {
        return TryParse(value, out var identity)
            ? identity
            : throw new FormatException($"Invalid node identity format: '{value}'.");
    }

    public static bool TryParse(string? value, out NodeIdentity identity)
    {
        identity = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf(_Separator);

        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var nodeIdValue = value[..separatorIndex];
        var incarnationValue = value[(separatorIndex + 1)..];

        if (!long.TryParse(incarnationValue, NumberStyles.None, CultureInfo.InvariantCulture, out var incarnation))
        {
            return false;
        }

        try
        {
            identity = new NodeIdentity(new NodeId(nodeIdValue), new NodeIncarnation(incarnation));

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{NodeId}{_Separator}{Incarnation.Value}"
        );
    }
}
