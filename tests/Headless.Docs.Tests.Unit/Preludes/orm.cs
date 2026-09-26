// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application entities, contexts, providers, and variables the ORM guide's examples assume.

global using static OrmAmbient;
using Couchbase;
using Couchbase.Transactions.Config;
using Microsoft.EntityFrameworkCore;

public sealed class Product : IEntity<Guid>
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public bool IsActive { get; set; }

    public IReadOnlyList<object> GetKeys() => [Id];
}

public sealed class Order
{
    public Guid Id { get; set; }

    public MoneyAmount Total { get; set; }
}

public sealed class Patient
{
    public Guid Id { get; set; }

    public string NationalId { get; set; } = "";

    public string CreditCardToken { get; set; } = "";

    public DateTime LastComputedAt { get; set; }
}

public sealed class InternalJob
{
    public Guid Id { get; set; }
}

public sealed class AppDbContext(HeadlessDbContextServices services, DbContextOptions<AppDbContext> options)
    : HeadlessDbContext(services, options)
{
    public DbSet<Product> Products => Set<Product>();

    public override string? DefaultSchema => null;
}

public sealed class MyClusterOptionsProvider : ICouchbaseClusterOptionsProvider
{
    public ValueTask<ClusterOptions> GetAsync(string clusterKey, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ClusterOptions());
}

public sealed class MyTransactionConfigProvider : ICouchbaseTransactionConfigProvider
{
    public ValueTask<TransactionConfigBuilder> GetAsync(
        string clusterKey,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(TransactionConfigBuilder.Create());
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class OrmAmbient
{
    public static string connectionString => "";

    public static ModelBuilder modelBuilder => null!;

    public static ModelConfigurationBuilder configurationBuilder => null!;

    public static AppDbContext dbContext => null!;

    public static IUnitOfWorkFactory unitOfWork => null!;

    public static IBucketContextProvider bucketContextProvider => null!;
}
