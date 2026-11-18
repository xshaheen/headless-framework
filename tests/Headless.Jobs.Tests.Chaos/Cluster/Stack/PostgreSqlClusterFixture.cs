// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Toxiproxy;

namespace Tests.Cluster;

[UsedImplicitly]
public sealed class PostgreSqlClusterFixture : ClusterStoreFixture
{
    private readonly PostgreSqlComponent _postgreSql = new();

    public override string Store => "PostgreSQL";

    protected override IAsyncLifetime Database => _postgreSql;

    protected override int DatabaseHostPort =>
        _postgreSql.Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    protected override async Task<string> CreateDatabaseAsync(string suffix, CancellationToken cancellationToken)
    {
        var database = _postgreSql.DerivedDatabaseName(suffix);
        await _postgreSql.CreateDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
        return _postgreSql.ConnectionStringFor(database);
    }

    public override string Proxied(string direct, ToxiproxyProxy route, string applicationName) =>
        new NpgsqlConnectionStringBuilder(direct)
        {
            Host = route.Host,
            Port = route.Port,
            ApplicationName = applicationName,
            // A cut route refuses at once; short timeouts keep a black-holed one from stalling a pool for long.
            Timeout = 5,
            CommandTimeout = 15,
        }.ConnectionString;

    public override void UseStore(DbContextOptionsBuilder db, string connectionString) =>
        db.UseNpgsql(connectionString);

    public override void UseClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> ef) =>
        ef.UsePostgreSqlClaims();

    public override void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString) =>
        setup.UsePostgreSql(connectionString);

    public override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public override void ClearPool(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    public override IJobsCoordinationFixture View(string connectionString) => new StoreView(connectionString);

    private sealed class PostgreSqlComponent : HeadlessPostgreSqlFixture
    {
        // A chaos run measures recovery under load, so its store keeps a server no other run shares.
        protected override bool ShareContainer => false;
    }

    /// <summary>The harness's PostgreSQL view of one run's database; the members a cluster run never uses throw.</summary>
    private sealed class StoreView(string connectionString) : IJobsCoordinationFixture
    {
        public string ConnectionString => connectionString;

        public StorageNamingStyle NamingStyle => StorageNamingStyle.SnakeCase;

        public string QualifiedTimeJobsTable => "headless.time_jobs";

        public string QualifiedCronJobsTable => "headless.cron_jobs";

        public string QualifiedCronJobOccurrencesTable => "headless.cron_job_occurrences";

        public string QualifyTable(string schema, string table) => $"{schema}.\"{table}\"";

        public string UtcNowSqlExpression => "now()";

        public string UtcNowOffsetSqlExpression(int seconds) =>
            string.Create(CultureInfo.InvariantCulture, $"(now() + interval '{seconds} seconds')");

        public string EfTranslatedDatabaseClockSql => "now()";

        public string LeaseDatabaseClockSql => "clock_timestamp()";

        public string ResetSql => throw new NotSupportedException("A cluster run gets a fresh database instead.");

        public string CreateProbeTableSql => throw new NotSupportedException();

        public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) =>
            setup.UsePostgreSql(connectionString);

        public void ConfigureStore(DbContextOptionsBuilder db) => db.UseNpgsql(connectionString);

        public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder) =>
            builder.UsePostgreSqlClaims();

        public DbConnection CreateConnection() => new NpgsqlConnection(connectionString);

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
public sealed class PostgreSqlClusterCollection : ICollectionFixture<PostgreSqlClusterFixture>
{
    public const string Name = "chaos-jobs-cluster-postgresql";
}
