// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Settings;

/// <summary>
/// Computes the version of a group of setting values in one provider scope: the opaque token a versioned read returns
/// and a versioned write must present.
/// </summary>
/// <remarks>
/// An <see cref="ISettingValueRecordRepository"/> implementation calls <see cref="Compute"/> inside the transaction of
/// <see cref="ISettingValueRecordRepository.SaveIfVersionAsync"/>, over the records it reads there, so the store and
/// every repository agree on the token.
/// </remarks>
public static class SettingValueVersion
{
    /// <summary>
    /// Returns the version of <paramref name="names"/> in the scope, from the records stored for them.
    /// </summary>
    /// <param name="providerName">The provider name of the scope.</param>
    /// <param name="providerKey">The resolved provider key of the scope, or <see langword="null"/> for host scope.</param>
    /// <param name="names">The names of the group. Order and duplicates do not matter.</param>
    /// <param name="records">The records stored for those names in that scope; records of other names are ignored.</param>
    /// <returns>
    /// A 22-character URL-safe token. It changes when any record of the group is inserted, updated, or deleted, and it
    /// differs between scopes and between name sets even when no record exists.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="providerKey"/> is <see langword="null"/>.</exception>
    public static string Compute(
        string providerName,
        string? providerKey,
        IEnumerable<string> names,
        IEnumerable<SettingValueRecord> records
    )
    {
        Argument.IsNotNull(providerName);
        Argument.IsNotNull(names);
        Argument.IsNotNull(records);

        var stampsByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (!stampsByName.TryGetValue(record.Name, out var stamps))
            {
                stamps = [];
                stampsByName[record.Name] = stamps;
            }

            // A row written before the store kept stamps hashes its id instead. Its first update stores a stamp,
            // which changes the hash as any other write does.
            stamps.Add(record.ConcurrencyStamp ?? ("id:" + record.Id.ToString("N")));
        }

        var builder = new StringBuilder();
        builder.Append(providerName).Append('\n').Append(providerKey is null ? "\0" : "=" + providerKey).Append('\n');

        foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            builder.Append(name).Append('\t');

            if (stampsByName.TryGetValue(name, out var stamps))
            {
                stamps.Sort(StringComparer.Ordinal);
                builder.AppendJoin(',', stamps);
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
