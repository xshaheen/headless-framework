// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class QueryableBehaviorTests : TestBase
{
    [Fact]
    public async Task should_return_an_entity_by_id_and_throw_a_typed_error_when_missing()
    {
        await using var fixture = await QueryFixture.CreateAsync(AbortToken);

        var found = await fixture.Context.Rows.FirstByIdAsync(2, AbortToken);
        Func<Task> missing = async () => await fixture.Context.Rows.FirstByIdAsync(99, AbortToken);

        found.Name.Should().Be("beta");
        var exception = await missing.Should().ThrowAsync<EntityNotFoundException>();
        exception.Which.Entity.Should().Be(nameof(QueryRow));
        exception.Which.Key.Should().Be("99");
    }

    [Fact]
    public async Task should_materialize_a_lookup_with_the_requested_projection_and_comparer()
    {
        await using var fixture = await QueryFixture.CreateAsync(AbortToken);

        var lookup = await fixture.Context.Rows.ToLookupAsync(
            row => row.Category,
            row => row.Name,
            StringComparer.OrdinalIgnoreCase,
            AbortToken
        );

        lookup.Should().HaveCount(2);
        lookup["group-a"].Should().Equal("alpha", "beta");
        lookup["GROUP-B"].Should().Equal("gamma");
    }

    [Fact]
    public async Task should_apply_data_grid_ordering_before_paging()
    {
        await using var fixture = await QueryFixture.CreateAsync(AbortToken);
        var request = new TestDataGridRequest
        {
            Orders = [new OrderBy(nameof(QueryRow.Name), Ascending: false)],
            Page = new TestIndexPageRequest { Index = 0, Size = 2 },
        };

        var page = await fixture.Context.Rows.ToDataGridAsync(request, AbortToken);

        page.TotalItems.Should().Be(3);
        page.Items.Select(row => row.Name).Should().Equal("gamma", "beta");
    }

    private sealed class QueryFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private QueryFixture(SqliteConnection connection, QueryDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public QueryDbContext Context { get; }

        public static async Task<QueryFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);
            var context = new QueryDbContext(
                new DbContextOptionsBuilder<QueryDbContext>().UseSqlite(connection).Options
            );

            try
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
                await context.Rows.AddRangeAsync(
                    [
                        new QueryRow
                        {
                            Id = 1,
                            Name = "alpha",
                            Category = "group-a",
                        },
                        new QueryRow
                        {
                            Id = 2,
                            Name = "beta",
                            Category = "GROUP-A",
                        },
                        new QueryRow
                        {
                            Id = 3,
                            Name = "gamma",
                            Category = "group-b",
                        },
                    ],
                    cancellationToken
                );
                await context.SaveChangesAsync(cancellationToken);
                return new QueryFixture(connection, context);
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

    private sealed class QueryDbContext(DbContextOptions<QueryDbContext> options) : DbContext(options)
    {
        public DbSet<QueryRow> Rows => Set<QueryRow>();
    }

    private sealed class QueryRow : Entity<int>
    {
        public required string Name { get; init; }

        public required string Category { get; init; }
    }

    private sealed class TestDataGridRequest : DataGridRequest;

    private sealed class TestIndexPageRequest : IndexPageRequest;
}
