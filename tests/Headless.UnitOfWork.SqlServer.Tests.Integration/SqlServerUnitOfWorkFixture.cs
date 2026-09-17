// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// SQL Server leaf fixture for the <c>RunAsync</c> conformance suite and the provider-specific scenarios. The
/// probe table is created outside any unit of work and probe counting uses an independent connection so it
/// observes committed state only. The tests share one probe table, so the collection runs serially.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerUnitOfWorkFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerUnitOfWorkFixture>,
        IUnitOfWorkRunFixture
{
    public async Task RunAsync(
        Func<IUnitOfWorkRunContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var connection = new SqlConnection(ConnectionString);

        await manager.RunAsync(
            connection,
            (unitOfWork, ct) => operation(new SqlServerRunContext(connection, unitOfWork), ct),
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT count(*) FROM probe_rows", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID('dbo.probe_rows', 'U') IS NULL CREATE TABLE dbo.probe_rows (id int IDENTITY PRIMARY KEY, name nvarchar(200)); DELETE FROM dbo.probe_rows;",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts one probe row on <paramref name="transaction" />; shared by the provider-specific scenarios.</summary>
    public static async Task InsertProbeRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            "INSERT INTO dbo.probe_rows (name) VALUES (@name)",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static ServiceProvider BuildProvider(CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });
        services.AddSqlServerUnitOfWork();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class SqlServerRunContext(SqlConnection connection, IUnitOfWork unitOfWork) : IUnitOfWorkRunContext
    {
        public IUnitOfWork UnitOfWork => unitOfWork;

        public Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            var transaction =
                (SqlTransaction?)(unitOfWork.Resource as IRelationalUnitOfWorkResource)?.Transaction
                ?? throw new InvalidOperationException("The helper exposed no live relational transaction.");

            return SqlServerUnitOfWorkFixture.InsertProbeRowAsync(connection, transaction, name, cancellationToken);
        }
    }
}
