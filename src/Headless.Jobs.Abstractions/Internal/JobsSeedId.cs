// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Jobs.Internal;

/// <summary>
/// Deterministic identities for seeded cron rows. Each row carries a marker that names its seed owner and its source,
/// and derives its primary key from that marker, so concurrent seeds of one owner converge on a single row through
/// primary-key dedup, and two owners that declare the same function never share a key. The optional Jobs distributed
/// lock only removes the redundant cross-node scan and write storm; it does not suppress duplicates.
/// </summary>
internal static class JobsSeedId
{
    /// <summary>
    /// The seed owner of a host that sets none. One shared value, not a per-executable one, so every host of an app,
    /// including an app split across executables, converges on one row per seeded definition.
    /// </summary>
    public const string DefaultOwner = "default";

    // Root of every seed marker. The owner segment ends at the next ':', which is why an owner cannot contain one.
    private const string _MarkerRoot = "headless.jobs.seed:";

    /// <summary>
    /// Returns <paramref name="owner"/> when it can scope seed markers: not blank, and free of <c>:</c>, which would
    /// let one owner's marker prefix match another owner's rows.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="owner"/> is blank or contains <c>:</c>.</exception>
    public static string ValidateOwner(string owner)
    {
        Argument.IsNotNullOrWhiteSpace(owner);

        if (owner.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Seed owner '{owner}' must not contain ':', which separates the owner from the rest of a seed marker.",
                nameof(owner)
            );
        }

        return owner;
    }

    /// <summary>The marker prefix every row seeded by <paramref name="owner"/> starts with.</summary>
    public static string OwnerMarkerPrefix(string owner)
    {
        return _MarkerRoot + owner + ":";
    }

    /// <summary>The marker of the row that <paramref name="owner"/> seeds for the attribute cron of a function.</summary>
    public static string CronSeedMarker(string owner, string function)
    {
        return OwnerMarkerPrefix(owner) + "cron:" + function;
    }

    /// <summary>Deterministic primary key of the row <paramref name="owner"/> seeds for a function's attribute cron.</summary>
    public static Guid ForCronSeed(string owner, string function)
    {
        return _ForMarker(CronSeedMarker(owner, function));
    }

    /// <summary>
    /// The marker of the row that <paramref name="owner"/> seeds for the recurring-job declaration keyed
    /// <paramref name="key"/>. Declarations have their own namespace, so a key equal to a function name never shares a
    /// row with that function's attribute cron.
    /// </summary>
    public static string DeclarationSeedMarker(string owner, string key)
    {
        return OwnerMarkerPrefix(owner) + "decl:" + key;
    }

    /// <summary>Deterministic primary key of the row <paramref name="owner"/> seeds for a recurring-job declaration.</summary>
    public static Guid ForDeclaration(string owner, string key)
    {
        return _ForMarker(DeclarationSeedMarker(owner, key));
    }

    /// <summary>
    /// Returns the retired form of an owned seed marker without changing its owner prefix. Repeated seeds must not
    /// nest the retired segment while a removed definition still has a running occurrence.
    /// </summary>
    public static string RetiredMarker(string marker)
    {
        var kindOffset = marker.IndexOf(':', _MarkerRoot.Length) + 1;
        return marker.AsSpan(kindOffset).StartsWith("retired:", StringComparison.Ordinal)
            ? marker
            : marker.Insert(kindOffset, "retired:");
    }

    /// <summary>Whether a stored marker is the retired form of a declared entry's live marker.</summary>
    public static bool IsRetiredMarkerOf(string? storedMarker, string liveMarker)
    {
        return string.Equals(storedMarker, RetiredMarker(liveMarker), StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects a seed list that a reconcile for <paramref name="owner"/> could not keep consistent: an entry whose
    /// marker belongs to another owner would never be deleted by this owner and could be deleted by the other, and two
    /// entries with one id would collide on the primary key.
    /// </summary>
    /// <exception cref="ArgumentException">The owner is invalid, or an entry is foreign or duplicated.</exception>
    public static void ValidateEntries(string owner, CronSeedDefinition[] entries)
    {
        ValidateOwner(owner);
        Argument.IsNotNull(entries);
        var prefix = OwnerMarkerPrefix(owner);
        var ids = new HashSet<Guid>();

        foreach (var entry in entries)
        {
            if (entry.Marker?.StartsWith(prefix, StringComparison.Ordinal) != true)
            {
                throw new ArgumentException(
                    $"Seed entry for function '{entry.Function}' has marker '{entry.Marker}', which is not owned by seed owner '{owner}'.",
                    nameof(entries)
                );
            }

            if (!ids.Add(entry.Id))
            {
                throw new ArgumentException(
                    $"Seed entry id '{entry.Id}' appears more than once in one seed.",
                    nameof(entries)
                );
            }
        }
    }

    private static Guid _ForMarker(string marker)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(marker));

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Stamp a well-formed deterministic GUID: version 8 (RFC 9562 custom) + RFC 4122 variant. The value only has
        // to be stable and collision-free across nodes for the same marker; it is generated and consumed by Jobs.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x80);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
