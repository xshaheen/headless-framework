// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

#pragma warning disable CA1303, MA0076 // Console report of a standalone scenario: not localized, and the invariant-culture numbers are only read by a person.

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Measures the durable relay against PostgreSQL: each message is stored, leased for dispatch, and marked succeeded,
/// three autonomous transactions. Also prices the envelope column write that a content refresh adds to a state
/// transition. Needs Docker: <c>dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- relay</c>.
/// </summary>
internal static class PostgreSqlRelayScenario
{
    private const int _Messages = 1000;

    public static async Task RunAsync()
    {
        await using var container = new PostgreSqlBuilder("postgres:18.1-alpine3.23").Build();
        await container.StartAsync();
        var connectionString = container.GetConnectionString();

        Console.WriteLine($"{"scenario", -34} {"msgs", 5} {"seconds", 8} {"msgs/s", 8} {"commits/msg", 12}");
        foreach (var concurrency in new[] { 1, 8, 32 })
        {
            await _RelayAsync(connectionString, concurrency);
        }

        Console.WriteLine();
        Console.WriteLine($"{"content write", -34} {"rows", 5} {"seconds", 8} {"updates/s", 10} {"MB written", 11}");
        foreach (var payloadBytes in new[] { 1024, 65536 })
        {
            await _ContentWriteAsync(connectionString, payloadBytes, refresh: false);
            await _ContentWriteAsync(connectionString, payloadBytes, refresh: true);
        }
    }

    private static async Task _RelayAsync(string connectionString, int publishConcurrency)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql(connectionString);
        });

        builder.Services.AddDeployStepsOnStartup();
        using var host = builder.Build();
        await host.StartAsync();

        await using var data = new NpgsqlDataSourceBuilder(connectionString).Build();
        await _ExecuteAsync(data, "TRUNCATE headless.messaging_published");
        var commitsBefore = await _CommitsAsync(data);
        var bus = host.Services.GetRequiredService<IBus>();
        var payload = new RelayMessage(new string('x', 1024));

        var clock = Stopwatch.StartNew();
        await Parallel.ForAsync(
            0,
            _Messages,
            new ParallelOptions { MaxDegreeOfParallelism = publishConcurrency },
            async (_, ct) => await bus.PublishAsync(payload, cancellationToken: ct)
        );

        while (
            await _ScalarAsync(
                data,
                "SELECT count(*) FROM headless.messaging_published WHERE status_name = 'Succeeded'"
            ) < _Messages
        )
        {
            await Task.Delay(10);
        }

        clock.Stop();
        var commits = await _CommitsAsync(data) - commitsBefore;
        Console.WriteLine(
            $"{$"relay, publish concurrency {publishConcurrency}", -34} {_Messages, 5} {clock.Elapsed.TotalSeconds, 8:F2} "
                + $"{_Messages / clock.Elapsed.TotalSeconds, 8:F0} {(double)commits / _Messages, 12:F2}"
        );

        await host.StopAsync();
    }

    private static async Task _ContentWriteAsync(string connectionString, int payloadBytes, bool refresh)
    {
        await using var data = new NpgsqlDataSourceBuilder(connectionString).Build();
        await _ExecuteAsync(data, "DROP TABLE IF EXISTS bench_content");
        await _ExecuteAsync(
            data,
            "CREATE TABLE bench_content (id int PRIMARY KEY, status_name text NOT NULL, content text NULL)"
        );
        var content = new string('x', payloadBytes);
        await using (
            var seed = data.CreateCommand(
                "INSERT INTO bench_content SELECT g, 'Scheduled', $1 FROM generate_series(1, $2) g"
            )
        )
        {
            seed.Parameters.AddWithValue(content);
            seed.Parameters.AddWithValue(_Messages);
            await seed.ExecuteNonQueryAsync();
        }

        await _ExecuteAsync(data, "VACUUM ANALYZE bench_content");

        var sql = refresh
            ? "UPDATE bench_content SET status_name = 'Succeeded', content = $2 WHERE id = $1"
            : "UPDATE bench_content SET status_name = 'Succeeded' WHERE id = $1";
        var newContent = new string('y', payloadBytes);
        var clock = Stopwatch.StartNew();
        for (var id = 1; id <= _Messages; id++)
        {
            await using var command = data.CreateCommand(sql);
            command.Parameters.AddWithValue(id);
            if (refresh)
            {
                command.Parameters.AddWithValue(newContent);
            }

            await command.ExecuteNonQueryAsync();
        }

        clock.Stop();
        var written = refresh ? (double)payloadBytes * _Messages / 1_000_000 : 0;
        Console.WriteLine(
            $"{$"{(refresh ? "Refresh" : "Preserve")}, {payloadBytes / 1024} KB", -34} {_Messages, 5} {clock.Elapsed.TotalSeconds, 8:F2} "
                + $"{_Messages / clock.Elapsed.TotalSeconds, 10:F0} {written, 11:F1}"
        );
    }

    private static async Task _ExecuteAsync(NpgsqlDataSource data, string sql)
    {
        await using var command = data.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> _ScalarAsync(NpgsqlDataSource data, string sql)
    {
        await using var command = data.CreateCommand(sql);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task<long> _CommitsAsync(NpgsqlDataSource data) =>
        _ScalarAsync(data, "SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()");

    private sealed record RelayMessage(string Body);
}
