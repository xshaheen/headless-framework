// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.Infrastructure;

// EF resolves connection-owning dependencies while initializing ContextServices, before GetService returns.
// Validate option mutations before initialization so a rejected OnConfiguring override cannot acquire caller ownership.
internal sealed class CoordinatedJobsDbContextOptions<TContext> : DbContextOptions<TContext>
    where TContext : DbContext
{
    internal CoordinatedJobsDbContextOptions(DbContextOptions options)
        : base(_ValidatedExtensions(options)) { }

    public override DbContextOptions WithExtension<TExtension>(TExtension extension) =>
        new CoordinatedJobsDbContextOptions<TContext>(base.WithExtension(extension));

    private static Dictionary<Type, IDbContextOptionsExtension> _ValidatedExtensions(DbContextOptions options)
    {
        var relational = RelationalOptionsExtension.Extract(options);
        if (relational.Connection is not null && relational.IsConnectionOwned)
        {
            throw new InvalidOperationException(
                "Coordinated Jobs contexts must not own an externally supplied connection. Configure contextOwnsConnection:false; the existing caller handles were not rebound or disposed."
            );
        }
        return options.Extensions.ToDictionary(extension => extension.GetType());
    }
}
