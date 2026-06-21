// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Incarnation-qualified node identity in the canonical <c>node@incarnation</c> form.</summary>
/// <remarks>
/// The <c>node@incarnation</c> string (for example <c>pod-a@3</c>) is used as the ownership stamp on all
/// coordinated resources. Because the incarnation is monotonically increasing, a higher value always
/// supersedes a lower one for the same node id, making stale-owner detection straightforward.
/// </remarks>
[PublicAPI]
public readonly record struct NodeIdentity(NodeId NodeId, NodeIncarnation Incarnation)
{
    private const char _Separator = '@';

    /// <summary>
    /// Parses a <c>node@incarnation</c> string into a <see cref="NodeIdentity"/>.
    /// </summary>
    /// <param name="value">The string to parse, for example <c>pod-a@3</c>.</param>
    /// <returns>The parsed <see cref="NodeIdentity"/>.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="value"/> does not conform to the <c>node@incarnation</c> format.
    /// </exception>
    public static NodeIdentity Parse(string value)
    {
        return TryParse(value, out var identity)
            ? identity
            : throw new FormatException($"Invalid node identity format: '{value}'.");
    }

    /// <summary>
    /// Attempts to parse a <c>node@incarnation</c> string into a <see cref="NodeIdentity"/>.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="identity">
    /// When this method returns <see langword="true"/>, the parsed identity; otherwise the default value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if parsing succeeded; <see langword="false"/> if <paramref name="value"/> is
    /// null, empty, or does not conform to the expected format.
    /// </returns>
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

    /// <summary>Returns the <c>node@incarnation</c> string representation of this identity.</summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{NodeId}{_Separator}{Incarnation.Value}");
    }
}
