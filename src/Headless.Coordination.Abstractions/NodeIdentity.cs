// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Represents an incarnation-qualified node identity in the canonical <c>node@incarnation</c> format.</summary>
/// <remarks>
/// The <c>node@incarnation</c> format (such as <c>pod-a@3</c>) serves as the ownership stamp across
/// coordinated resources. The monotonically increasing incarnation allows higher values to supersede lower values
/// for the same node identifier, supporting stale-owner detection.
/// </remarks>
[PublicAPI]
public readonly record struct NodeIdentity(NodeId NodeId, NodeIncarnation Incarnation)
{
    private const char _Separator = '@';

    /// <summary>
    /// Parses a string formatted as <c>node@incarnation</c> into a <see cref="NodeIdentity"/>.
    /// </summary>
    /// <param name="value">The string to parse, such as <c>pod-a@3</c>.</param>
    /// <returns>The parsed <see cref="NodeIdentity"/>.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> does not conform to the <c>node@incarnation</c> format.</exception>
    public static NodeIdentity Parse(string value)
    {
        return TryParse(value, out var identity)
            ? identity
            : throw new FormatException($"Invalid node identity format: '{value}'.");
    }

    /// <summary>
    /// Attempts to parse a string formatted as <c>node@incarnation</c> into a <see cref="NodeIdentity"/>.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="identity">
    /// When this method returns <see langword="true"/>, contains the parsed identity; otherwise, the default value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.
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

    /// <summary>Returns the string representation of this identity formatted as <c>node@incarnation</c>.</summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{NodeId}{_Separator}{Incarnation.Value}");
    }
}
