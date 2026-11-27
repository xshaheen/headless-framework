// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.Coordination;

namespace Headless.Cluster;

/// <summary>
/// The node change cursor: what the shared membership store last showed, as each incarnation with a version of what
/// the dashboard shows of it. The text form is <c>v1:</c> followed by the base64url of one <c>identity version</c>
/// line per incarnation, in ordinal order, so every node builds the same cursor from the same store read.
/// </summary>
/// <remarks>
/// <para>
/// The store has no change marker to read from: Suspected and Dead are derived from a heartbeat's age at read time,
/// so no write ever marks them. The cursor therefore carries the last view and each read compares against it.
/// </para>
/// <para>
/// Each identity is itself base64url-encoded inside its line: a node id may hold an interior space or line break, and
/// written verbatim either one would split the identity from its version or one incarnation into two.
/// </para>
/// </remarks>
internal static class NodeChangeCursor
{
    private const string _Prefix = "v1:";
    private const int _VersionLength = 16;

    public static string Format(IReadOnlyDictionary<string, string> versions)
    {
        var text = string.Join(
            '\n',
            versions
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(pair.Key)) + " " + pair.Value)
        );

        return _Prefix + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Reads a cursor's incarnations and their versions.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a node change cursor.</exception>
    public static Dictionary<string, string> Parse(string cursor)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!cursor.StartsWith(_Prefix, StringComparison.Ordinal))
        {
            throw _Invalid(cursor);
        }

        byte[] bytes;

        try
        {
            bytes = Base64Url.DecodeFromChars(cursor.AsSpan(_Prefix.Length));
        }
        catch (FormatException)
        {
            throw _Invalid(cursor);
        }

        if (bytes.Length == 0)
        {
            return versions;
        }

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            var separator = line.IndexOf(' ', StringComparison.Ordinal);

            if (
                separator <= 0
                || line.Length - separator - 1 != _VersionLength
                || _DecodeIdentity(line.AsSpan(0, separator)) is not { } identity
                || !NodeIdentity.TryParse(identity, out _)
                || !versions.TryAdd(identity, line[(separator + 1)..])
            )
            {
                throw _Invalid(cursor);
            }
        }

        return versions;
    }

    /// <summary>
    /// The version of what the dashboard shows of a node: its state, role, and metadata. The heartbeat time is left
    /// out, or every beat would count as a change.
    /// </summary>
    public static string Version(NodeLivenessSnapshot node)
    {
        var text = new StringBuilder()
            .Append(node.State.ToString())
            .Append('\n')
            .Append(node.Role ?? "\0")
            .Append('\n');

        foreach (var (key, value) in node.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('=').Append(value).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));

        return Convert.ToHexStringLower(hash.AsSpan(0, _VersionLength / 2));
    }

    private static string? _DecodeIdentity(ReadOnlySpan<char> encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ArgumentException _Invalid(string cursor) =>
        new($"'{cursor}' is not a Cluster node change cursor.", nameof(cursor));
}
