// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tests;

public sealed class CountPerMonthExtensionsTests : TestBase
{
    [Fact]
    public async Task should_group_counts_in_database_when_date_only_count_per_month()
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

    [Fact]
    public async Task should_reject_an_inverted_date_time_offset_range_before_query_execution()
    {
        await using var fixture = await MonthCountFixture.CreateAsync(AbortToken);
        fixture.CommandLog.Clear();
        var offset = TimeSpan.FromHours(2);

        Func<Task> action = async () =>
            await fixture.Context.Rows.CountPerMonthAsync(
                static row => row.DateTimeOffsetValue,
                new DateTimeOffset(2026, 4, 20, 0, 0, 0, offset),
                new DateTimeOffset(2026, 1, 15, 0, 0, 0, offset),
                AbortToken
            );

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("start");
        fixture.CommandLog.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task should_compile_date_only_count_per_month_once_for_any_dates()
    {
        // given
        await using var fixture = await MonthCountFixture.CreateAsync(AbortToken);
        await _CompilationsAsync(fixture, _ => new DateOnly(2026, 1, 15));
        var sameDates = await _CompilationsAsync(fixture, _ => new DateOnly(2026, 1, 15));

        // when
        var distinctDates = await _CompilationsAsync(fixture, call => new DateOnly(2025, 1 + call, 15));

        // then: the dates are query parameters, so new dates reuse the compiled query
        distinctDates.Should().Be(sameDates);
    }

    [Fact]
    public async Task should_compile_date_time_offset_count_per_month_once_for_any_dates()
    {
        // given
        await using var fixture = await MonthCountFixture.CreateAsync(AbortToken);
        var offset = TimeSpan.FromHours(2);
        await _CompilationsAsync(fixture, _ => new DateTimeOffset(2026, 1, 15, 0, 0, 0, offset));
        var sameDates = await _CompilationsAsync(fixture, _ => new DateTimeOffset(2026, 1, 15, 0, 0, 0, offset));

        // when
        var distinctDates = await _CompilationsAsync(
            fixture,
            call => new DateTimeOffset(2025, 1 + call, 15, 0, 0, 0, offset)
        );

        // then
        distinctDates.Should().Be(sameDates);
    }

    private static async Task<int> _CompilationsAsync(MonthCountFixture fixture, Func<int, DateOnly> start)
    {
        fixture.Compilations = 0;

        for (var call = 0; call < 3; call++)
        {
            var from = start(call);
            _ = await fixture.Context.Rows.CountPerMonthAsync(
                static row => row.DateOnlyValue,
                from,
                from.AddMonths(4),
                AbortToken
            );
        }

        return fixture.Compilations;
    }

    private static async Task<int> _CompilationsAsync(MonthCountFixture fixture, Func<int, DateTimeOffset> start)
    {
        fixture.Compilations = 0;

        for (var call = 0; call < 3; call++)
        {
            var from = start(call);
            _ = await fixture.Context.Rows.CountPerMonthAsync(
                static row => row.DateTimeOffsetValue,
                from,
                from.AddMonths(4),
                AbortToken
            );
        }

        return fixture.Compilations;
    }

    private static bool _IsGroupedCountSql(string sql)
    {
        return sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MonthCountFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly CompilationCounter _compilations;

        private MonthCountFixture(
            SqliteConnection connection,
            MonthCountDbContext context,
            CommandLog commandLog,
            CompilationCounter compilations
        )
        {
            _connection = connection;
            _compilations = compilations;
            Context = context;
            CommandLog = commandLog;
        }

        public MonthCountDbContext Context { get; }

        public CommandLog CommandLog { get; }

        public int Compilations
        {
            get => _compilations.Count;
            set => _compilations.Count = value;
        }

        public static async Task<MonthCountFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);

            var commandLog = new CommandLog();
            var compilations = new CompilationCounter();
            var options = new DbContextOptionsBuilder<MonthCountDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(commandLog)
                .LogTo(
                    _ => compilations.Count++,
                    (id, _) => id == CoreEventId.QueryCompilationStarting,
                    DbContextLoggerOptions.None
                )
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

                return new(connection, context, commandLog, compilations);
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

        // SQLite cannot compare DateTimeOffset columns; a binary column keeps the date-range predicate translatable.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<MonthCountRow>()
                .Property(x => x.DateTimeOffsetValue)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
        }
    }

    private sealed class MonthCountRow
    {
        public int Id { get; set; }

        public DateOnly DateOnlyValue { get; init; }

        public DateTimeOffset DateTimeOffsetValue { get; init; }
    }

    private sealed class CompilationCounter
    {
        public int Count { get; set; }
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands => _commands;

        public void Clear()
        {
            _commands.Clear();
        }

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
