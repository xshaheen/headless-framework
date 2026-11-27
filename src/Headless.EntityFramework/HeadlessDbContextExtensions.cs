// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension methods for <see cref="DbContext"/>.</summary>
[PublicAPI]
public static class HeadlessDbContextExtensions
{
    /// <summary>
    /// Resolves <typeparamref name="T"/> from the EF Core internal service provider, returning
    /// <see langword="null"/> when the service is not registered instead of throwing.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <param name="infrastructure">An EF Core infrastructure accessor (typically a <c>DbContext</c>).</param>
    /// <returns>The resolved service, or <see langword="null"/> if not registered.</returns>
    public static T? GetServiceOrDefault<T>(this IInfrastructure<IServiceProvider> infrastructure)
        where T : class
    {
        try
        {
            return infrastructure.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            return null; // GetService throw on service not found
        }
    }

    /// <summary>
    /// Saves the context's changes and turns a violation of the unique key, unique index, or foreign key named
    /// <paramref name="constraintName" /> into a <see cref="ConflictException" /> carrying <paramref name="conflict" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it where a handler checks that no other row holds a value and a concurrent writer can still take the value
    /// before the save. The index that enforces the value decides the race, and the caller gets its own error instead
    /// of the generic <c>g:unique_violation</c>.
    /// </para>
    /// <para>
    /// Name a foreign key to answer a delete of a row that other rows still reference, such as a category in use, with
    /// your own conflict: the key refuses the delete even when a referencing row was added after the handler's check.
    /// </para>
    /// <para>
    /// Any other failure, a violation of another constraint included, propagates unchanged. See
    /// <see cref="RelationalFaults.IsUniqueViolation(Exception, out string?)" /> and
    /// <see cref="RelationalFaults.IsReferenceViolation(Exception, out string?)" /> for which engines report the name.
    /// </para>
    /// </remarks>
    /// <param name="context">The context to save.</param>
    /// <param name="constraintName">
    /// The violated key or unique index, as the database names it (for an EF index, its <c>GetDatabaseName()</c>; for a
    /// foreign key, its <c>GetConstraintName()</c>), compared ordinally.
    /// </param>
    /// <param name="conflict">The error the conflict carries.</param>
    /// <param name="cancellationToken">The token to cancel the save.</param>
    /// <returns>The number of state entries written to the database.</returns>
    /// <exception cref="ConflictException">The save violated <paramref name="constraintName" />.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="constraintName" /> is empty.</exception>
    public static Task<int> SaveChangesOrConflictAsync(
        this DbContext context,
        string constraintName,
        ErrorDescriptor conflict,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(constraintName);
        Argument.IsNotNull(conflict);

        return context.SaveChangesOrConflictAsync(
            new Dictionary<string, ErrorDescriptor>(StringComparer.Ordinal) { [constraintName] = conflict },
            cancellationToken
        );
    }

    /// <summary>
    /// Saves the context's changes and turns a unique-key or foreign-key violation of a constraint in
    /// <paramref name="conflicts" /> into a <see cref="ConflictException" /> carrying that constraint's error.
    /// </summary>
    /// <remarks>
    /// The dictionary's comparer matches the name the database reports; build it with <see cref="StringComparer.Ordinal" />
    /// unless your names need otherwise. A violation of a constraint the dictionary does not hold, a violation whose
    /// engine reported no name, and any other failure propagate unchanged. The violation is matched wherever the save
    /// reports it, a commit of the context's own transaction included.
    /// </remarks>
    /// <param name="context">The context to save.</param>
    /// <param name="conflicts">The error for each key or unique index name.</param>
    /// <param name="cancellationToken">The token to cancel the save.</param>
    /// <returns>The number of state entries written to the database.</returns>
    /// <exception cref="ConflictException">The save violated a constraint in <paramref name="conflicts" />.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    public static async Task<int> SaveChangesOrConflictAsync(
        this DbContext context,
        IReadOnlyDictionary<string, ErrorDescriptor> conflicts,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(conflicts);

        try
        {
            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (_ViolatedConstraintName(exception) is { } constraintName
                && conflicts.TryGetValue(constraintName, out var conflict)
            )
        {
            throw new ConflictException(conflict, exception);
        }
    }

    private static string? _ViolatedConstraintName(Exception exception)
    {
        if (RelationalFaults.IsUniqueViolation(exception, out var constraintName))
        {
            return constraintName;
        }

        return RelationalFaults.IsReferenceViolation(exception, out constraintName) ? constraintName : null;
    }

    /// <summary>
    /// The application service provider EF Core was built with (set by the Headless registration via
    /// <c>UseApplicationServiceProvider</c>), or <see langword="null"/> for a hand-constructed <c>DbContextOptions</c>
    /// outside the DI pipeline — in which case the generators fall back to their framework default.
    /// </summary>
    internal static IServiceProvider? GetApplicationServices(this DbContext context)
    {
        return context
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider;
    }
}
