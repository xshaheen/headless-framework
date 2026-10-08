// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.DistributedLocks;

/// <summary>
/// Builds lock resource names from an entity type and its id, so every caller that locks one entity derives the same
/// name without inventing its own string family.
/// </summary>
/// <remarks>
/// <para>
/// The name is <c>"{typeof(T).Name}:{id}"</c>, a plain string, so it goes anywhere a resource name does:
/// <c>unit.TransactionLocks</c>, <see cref="IDistributedLock" />, and the multi-key <c>AcquireAll</c> forms. The
/// provider's key prefix is added later, by the provider, exactly as for a hand-written name.
/// </para>
/// <para>
/// The short type name is deliberate. Two types that share a name in different namespaces map to one name, which
/// only makes them contend with each other; it never lets two holders of one entity run together. Renaming the type
/// does change the name, so during a rolling deployment the old and new versions do not exclude each other on that
/// entity until every node runs the new name.
/// </para>
/// </remarks>
[PublicAPI]
public static class LockKey
{
    /// <summary>Builds the resource name for the <typeparamref name="T" /> identified by <paramref name="id" />.</summary>
    /// <typeparam name="T">The entity type the lock protects.</typeparam>
    /// <param name="id">The entity's id, used as is.</param>
    /// <returns><c>"{typeof(T).Name}:{id}"</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="id" /> is empty or whitespace.</exception>
    public static string For<T>(string id)
    {
        Argument.IsNotNullOrWhiteSpace(id);

        return typeof(T).Name + ":" + id;
    }

    /// <summary>Builds the resource name for the <typeparamref name="T" /> identified by <paramref name="id" />.</summary>
    /// <typeparam name="T">The entity type the lock protects.</typeparam>
    /// <param name="id">The entity's id, formatted as lowercase hyphenated hex (<c>"D"</c>).</param>
    /// <returns><c>"{typeof(T).Name}:{id}"</c>.</returns>
    public static string For<T>(Guid id)
    {
        return typeof(T).Name + ":" + id.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the resource name for the <typeparamref name="T" /> identified by <paramref name="id" />.</summary>
    /// <typeparam name="T">The entity type the lock protects.</typeparam>
    /// <param name="id">The entity's id, formatted with the invariant culture; an <see cref="int" /> id widens to it.</param>
    /// <returns><c>"{typeof(T).Name}:{id}"</c>.</returns>
    public static string For<T>(long id)
    {
        return typeof(T).Name + ":" + id.ToString(CultureInfo.InvariantCulture);
    }
}
