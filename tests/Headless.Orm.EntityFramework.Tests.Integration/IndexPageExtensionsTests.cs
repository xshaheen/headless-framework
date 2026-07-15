// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class IndexPageExtensionsTests : TestBase
{
    [Fact]
    public async Task should_return_partial_last_page_when_negative_index_targets_last_page()
    {
        // given
        await using var fixture = await SqlitePageFixture.CreateAsync(AbortToken);

        // when
        var page = await fixture
            .Context.Rows.OrderBy(static row => row.Position)
            .ToIndexPageAsync(index: -1, size: 4, cancellationToken: AbortToken);

        // then
        page.Index.Should().Be(2);
        page.TotalItems.Should().Be(9);
        page.TotalPages.Should().Be(3);
        page.HasPrevious.Should().BeTrue();
        page.HasNext.Should().BeFalse();
        page.Items.Select(static row => row.Position).Should().Equal(9);
    }

    [Fact]
    public async Task should_translate_negative_index_page_to_sql_skip_take_when_ordered()
    {
        // given
        await using var fixture = await SqlitePageFixture.CreateAsync(AbortToken);

        // when
        var page = await fixture.Context.Rows.ToIndexPageAsync(
            static row => row.Position,
            ascending: true,
            index: -2,
            size: 4,
            cancellationToken: AbortToken
        );

        // then
        page.Index.Should().Be(1);
        page.Items.Select(static row => row.Position).Should().Equal(5, 6, 7, 8);
    }

    [Fact]
    public async Task should_translate_negative_index_projected_page_to_sql_skip_take_when_ordered()
    {
        // given
        await using var fixture = await SqlitePageFixture.CreateAsync(AbortToken);

        // when
        var page = await fixture.Context.Rows.ToIndexPageAsync(
            static row => row.Position,
            ascending: true,
            static row => row.Name,
            index: -1,
            size: 4,
            cancellationToken: AbortToken
        );

        // then
        page.Index.Should().Be(2);
        page.Items.Should().Equal("row-9");
    }

    private sealed class SqlitePageFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqlitePageFixture(SqliteConnection connection, PageDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public PageDbContext Context { get; }

        public static async Task<SqlitePageFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<PageDbContext>().UseSqlite(connection).Options;
            var context = new PageDbContext(options);

            try
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);

                context.Rows.AddRange(
                    Enumerable
                        .Range(1, 9)
                        .Select(static index => new PageRow { Position = index, Name = $"row-{index}" })
                );

                await context.SaveChangesAsync(cancellationToken);

                return new(connection, context);
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

    private sealed class PageDbContext(DbContextOptions<PageDbContext> options) : DbContext(options)
    {
        public DbSet<PageRow> Rows => Set<PageRow>();
    }

    private sealed class PageRow
    {
        public int Id { get; set; }

        public int Position { get; set; }

        public required string Name { get; init; }
    }
}
