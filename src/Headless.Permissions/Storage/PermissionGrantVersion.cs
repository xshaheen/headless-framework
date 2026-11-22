// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Permissions;

/// <summary>
/// Computes the version of a group of permission grants for one provider target in one tenant: the opaque token a
/// versioned read returns and a versioned write must present.
/// </summary>
/// <remarks>
/// The version needs no stored stamp because a grant record is never updated in place: changing a grant to a denial
/// (or back) deletes the record and inserts a new one with a new <see cref="PermissionGrantRecord"/> id. An
/// <see cref="IPermissionGrantRepository"/> implementation must keep that rule, and calls <see cref="Compute"/> inside
/// the transaction of <see cref="IPermissionGrantRepository.SaveIfVersionAsync"/> over the records it reads there.
/// </remarks>
public static class PermissionGrantVersion
{
    /// <summary>Returns the version of <paramref name="names"/> for the target, from the records stored for them.</summary>
    /// <param name="providerName">The grant provider name, for example <c>"Role"</c>.</param>
    /// <param name="providerKey">The provider target, for example a role name.</param>
    /// <param name="tenantId">The tenant whose records were read, or <see langword="null"/> for the host.</param>
    /// <param name="names">The permission names of the group. Order and duplicates do not matter.</param>
    /// <param name="records">The records stored for those names; records of other names are ignored.</param>
    /// <returns>
    /// A 22-character URL-safe token. It changes when any record of the group is inserted or deleted, and it differs
    /// between targets, tenants, and name sets even when no record exists.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="providerName"/>, <paramref name="providerKey"/>, <paramref name="names"/>, or
    /// <paramref name="records"/> is <see langword="null"/>.
    /// </exception>
    public static string Compute(
        string providerName,
        string providerKey,
        string? tenantId,
        IEnumerable<string> names,
        IEnumerable<PermissionGrantRecord> records
    )
    {
        Argument.IsNotNull(providerName);
        Argument.IsNotNull(providerKey);
        Argument.IsNotNull(names);
        Argument.IsNotNull(records);

        var tokensByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (!tokensByName.TryGetValue(record.Name, out var tokens))
            {
                tokens = [];
                tokensByName[record.Name] = tokens;
            }

            tokens.Add(record.Id.ToString("N") + (record.IsGranted ? "+" : "-"));
        }

        var builder = new StringBuilder();
        builder
            .Append(providerName.Length)
            .Append(':')
            .Append(providerName)
            .Append('\n')
            .Append(providerKey.Length)
            .Append(':')
            .Append(providerKey)
            .Append('\n')
            .Append(tenantId is null ? "-" : tenantId.Length.ToString(CultureInfo.InvariantCulture) + ":" + tenantId)
            .Append('\n');

        foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            builder.Append(name.Length).Append(':').Append(name).Append('\t');

            if (tokensByName.TryGetValue(name, out var tokens))
            {
                tokens.Sort(StringComparer.Ordinal);
                builder.AppendJoin(',', tokens);
            }
            else
            {
                builder.Append('-');
            }

            builder.Append('\n');
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()), hash);

        return Base64Url.EncodeToString(hash[..16]);
    }
}
