// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Toxiproxy;

namespace Tests.Cluster;

[UsedImplicitly]
public sealed class SqlServerClusterFixture : ClusterStoreFixture
{
    private readonly SqlServerComponent _sqlServer = new();

    public override string Store => "SQL Server";

    protected override IAsyncLifetime Database => _sqlServer;

    protected override int DatabaseHostPort =>
        int.Parse(
            new SqlConnectionStringBuilder(_sqlServer.ConnectionString).DataSource.Split(',')[1],
            CultureInfo.InvariantCulture
        );

    protected override async Task<string> CreateDatabaseAsync(string suffix, CancellationToken cancellationToken)
    {
        var database = _sqlServer.DerivedDatabaseName(suffix);
        await _sqlServer.CreateDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
        return _sqlServer.ConnectionStringFor(database);
    }

    public override string Proxied(string direct, ToxiproxyProxy route, string applicationName) =>
        new SqlConnectionStringBuilder(direct)
        {
            DataSource = $"{route.Host},{route.Port.ToString(CultureInfo.InvariantCulture)}",
            ApplicationName = applicationName,
            // A cut route refuses at once; short timeouts keep a black-holed one from stalling a pool for long.
            ConnectTimeout = 5,
            CommandTimeout = 15,
        }.ConnectionString;

    public override void UseStore(DbContextOptionsBuilder db, string connectionString) =>
        db.UseSqlServer(connectionString);

    public override void UseClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> ef) => ef.UseSqlServerClaims();

    public override void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString) =>
        setup.UseSqlServer(connectionString);

    public override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public override void ClearPool(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        SqlConnection.ClearPool(connection);
    }

    public override IJobsCoordinationFixture View(string connectionString) => new StoreView(connectionString);

    private sealed class SqlServerComponent : HeadlessSqlServerFixture
    {
        // A chaos run measures recovery under load, so its store keeps a server no other run shares.
        protected override bool ShareContainer => false;
    }

    /// <summary>The harness's SQL Server view of one run's database; the members a cluster run never uses throw.</summary>
    private sealed class StoreView(string connectionString) : IJobsCoordinationFixture
    {
        public string ConnectionString => connectionString;

        public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

        public string QualifiedTimeJobsTable => "[headless].[TimeJobs]";

        public string QualifiedCronJobsTable => "[headless].[CronJobs]";

        public string QualifiedCronJobOccurrencesTable => "[headless].[CronJobOccurrences]";

        public string QualifyTable(string schema, string table) => $"[{schema}].[{table}]";

        public string UtcNowSqlExpression => "SYSUTCDATETIME()";

        public string UtcNowOffsetSqlExpression(int seconds) =>
            string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, {seconds}, SYSUTCDATETIME())");

        public string EfTranslatedDatabaseClockSql => "GETUTCDATE()";

        public string LeaseDatabaseClockSql => "SYSUTCDATETIME()";

        public string ResetSql => throw new NotSupportedException("A cluster run gets a fresh database instead.");

        public string CreateProbeTableSql => throw new NotSupportedException();

        public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) =>
            setup.UseSqlServer(connectionString);

        public void ConfigureStore(DbContextOptionsBuilder db) => db.UseSqlServer(connectionString);

        public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder) =>
            builder.UseSqlServerClaims();

        public DbConnection CreateConnection() => new SqlConnection(connectionString);

        public void ConfigureUnitOfWork(IServiceCollection services) => throw new NotSupportedException();

        public void ConfigureMessagingStorage(MessagingSetupBuilder setup) => throw new NotSupportedException();

        public Task RunCoordinatedTransactionAsync(
            IServiceProvider services,
            Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerClusterCollection : ICollectionFixture<SqlServerClusterFixture>
{
    public const string Name = "chaos-jobs-cluster-sqlserver";
}
