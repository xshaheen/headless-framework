// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

public sealed class CountPerMonthExtensionsTests : TestBase
{
    [Fact]
    public async Task date_only_count_per_month_should_group_counts_in_database()
    {
        // given
        await using var fixture = await MonthCountFixture.CreateAsync(AbortToken);
        fixture.CommandLog.Clear();

        // when
        var buckets = (
            await fixture.Context.Rows.CountPerMonthAsync(
                static row => row.DateOnlyValue,
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 4, 20),
                AbortToken
            )
        ).ToArray();

        // then
        buckets
            .Should()
            .Equal(
                new EntityPerDateOnly(new DateOnly(2026, 2, 1), 2),
                new EntityPerDateOnly(new DateOnly(2026, 3, 1), 1),
                new EntityPerDateOnly(new DateOnly(2026, 4, 1), 0)
            );
        fixture.CommandLog.Commands.Any(_IsGroupedCountSql).Should().BeTrue();
    }

    private static bool _IsGroupedCountSql(string sql)
    {
        return sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MonthCountFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private MonthCountFixture(SqliteConnection connection, MonthCountDbContext context, CommandLog commandLog)
        {
            _connection = connection;
            Context = context;
            CommandLog = commandLog;
        }

        public MonthCountDbContext Context { get; }

        public CommandLog CommandLog { get; }

        public static async Task<MonthCountFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);

            var commandLog = new CommandLog();
            var options = new DbContextOptionsBuilder<MonthCountDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(commandLog)
                .Options;
            var context = new MonthCountDbContext(options);

            try
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);

                await context.Rows.AddRangeAsync(
                    [
                        new MonthCountRow
                        {
                            DateOnlyValue = new DateOnly(2026, 2, 2),
                            DateTimeOffsetValue = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.FromHours(2)),
                        },
                        new MonthCountRow
                        {
                            DateOnlyValue = new DateOnly(2026, 2, 20),
                            DateTimeOffsetValue = new DateTimeOffset(2026, 2, 20, 0, 0, 0, TimeSpan.FromHours(2)),
                        },
                        new MonthCountRow
                        {
                            DateOnlyValue = new DateOnly(2026, 3, 5),
                            DateTimeOffsetValue = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.FromHours(2)),
                        },
                        new MonthCountRow
                        {
                            DateOnlyValue = new DateOnly(2026, 5, 1),
                            DateTimeOffsetValue = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(2)),
                        },
                    ],
                    cancellationToken
                );

                await context.SaveChangesAsync(cancellationToken);

                return new(connection, context, commandLog);
            }
            catch
            {
                await context.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MonthCountDbContext(DbContextOptions<MonthCountDbContext> options) : DbContext(options)
    {
        public DbSet<MonthCountRow> Rows => Set<MonthCountRow>();
    }

    private sealed class MonthCountRow
    {
        public int Id { get; set; }

        public DateOnly DateOnlyValue { get; init; }

        public DateTimeOffset DateTimeOffsetValue { get; init; }
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            _commands.Add(command.CommandText);

            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }
}
