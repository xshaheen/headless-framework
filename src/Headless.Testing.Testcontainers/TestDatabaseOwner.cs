// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Names the databases one fixture owns on a server that other fixtures and processes may share.
/// </summary>
/// <remarks>
/// Every name starts with <see cref="Prefix"/> and a random owner id, so <c>hf_3f9a0c12b7de</c> is the fixture's own
/// database and <c>hf_3f9a0c12b7de_fencing_other</c> one it derived from it. The owner holds a session lock named
/// after its id for its whole lifetime; a sweep that can take that lock knows the owner's process is gone and drops
/// every database that carries the id.
/// </remarks>
internal static class TestDatabaseOwner
{
    private static readonly SearchValues<char> _HexDigits = SearchValues.Create("0123456789abcdef");
    private static readonly SearchValues<char> _SuffixCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyz0123456789_"
    );

    /// <summary>The prefix of every database a fixture owns.</summary>
    public const string Prefix = "hf_";

    /// <summary>The length of an owner id, the prefix included.</summary>
    public const int IdLength = 15;

    // PostgreSQL truncates identifiers past 63 bytes without an error, which would give two derived names one database.
    private const int _MaxNameLength = 63;

    /// <summary>Returns a new owner id.</summary>
    public static string NewId()
    {
        return Prefix + Guid.NewGuid().ToString("N")[..(IdLength - Prefix.Length)];
    }

    /// <summary>Returns the owner id <paramref name="database"/> belongs to, or <see langword="null"/> when none.</summary>
    public static string? OwnerOf(string database)
    {
        if (database.Length < IdLength || !database.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = database[..IdLength];

        return id.AsSpan(Prefix.Length).ContainsAnyExcept(_HexDigits) ? null
            : database.Length == IdLength || database[IdLength] == '_' ? id
            : null;
    }

    /// <summary>Returns the name of a database derived from owner <paramref name="ownerId"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The suffix is empty, holds a character other than a-z, 0-9, or _, or makes the name longer than 63 characters.
    /// </exception>
    public static string Derived(string ownerId, string suffix)
    {
        if (
            suffix.Length == 0
            || suffix.AsSpan().ContainsAnyExcept(_SuffixCharacters)
            || ownerId.Length + 1 + suffix.Length > _MaxNameLength
        )
        {
            throw new ArgumentException(
                "A database suffix must be non-empty, use only lowercase letters, digits, and underscores, and keep the name within 63 characters.",
                nameof(suffix)
            );
        }

        return ownerId + "_" + suffix;
    }

    /// <summary>The name of the session lock owner <paramref name="ownerId"/> holds while it lives.</summary>
    public static string LockName(string ownerId)
    {
        return "headless_testing_owner:" + ownerId;
    }
}
