// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

// EF resolves connection-owning dependencies while initializing ContextServices, before GetService returns.
// Validate option mutations before initialization so a rejected OnConfiguring override cannot acquire caller ownership.
internal sealed class CoordinatedJobsDbContextOptions<TContext> : DbContextOptions<TContext>
    where TContext : DbContext
{
    // A coordinated context runs inside the caller's transaction, which only the caller can replay. A retrying
    // strategy refuses to run over a transaction it did not begin, so the context runs every operation once and leaves
    // the replay to the caller's unit. One shared delegate keeps every coordinated context on the same options shape.
    private static readonly Func<ExecutionStrategyDependencies, IExecutionStrategy> _NonRetryingStrategy =
        static dependencies => new NonRetryingExecutionStrategy(dependencies);

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

        var extensions = options.Extensions.ToDictionary(extension => extension.GetType());
        extensions[relational.GetType()] = relational.WithExecutionStrategyFactory(_NonRetryingStrategy);
        return extensions;
    }
}
