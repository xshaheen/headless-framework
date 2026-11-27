// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed partial class ClassifiedObjectProjectionTests
{
    [Fact]
    public async Task should_surface_the_update_exception_of_a_failed_save_on_a_context_with_an_unmapped_set()
    {
        // given: a context, shaped like an IdentityDbContext that excludes passkeys, whose DbSet for an entity the
        // model ignores throws when enumerated; EF Core logs a failed save with the exception as a template value
        using var sink = new CapturingProvider();
        await using var provider = _Build(sink);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        var options = new DbContextOptionsBuilder<AccountsContext>()
            .UseSqlite(connection)
            .UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>())
            .Options;

        await using var context = new AccountsContext(options);
        await context.Database.EnsureCreatedAsync(AbortToken);
        context.Accounts.Add(new Account { Email = _Email });
        context.Accounts.Add(new Account { Email = _Email });

        // when
        var save = () => context.SaveChangesAsync(AbortToken);

        // then: the caller gets EF Core's own exception, and the failure is still logged
        var thrown = await save.Should().ThrowExactlyAsync<DbUpdateException>();
        sink.Entries.Should()
            .Contain(entry => entry.Properties.Values.Contains(thrown.Which))
            .Which.Text.Should()
            .Contain(nameof(AccountsContext))
            .And.Contain("UNIQUE constraint failed");
    }

    public sealed class Account
    {
        public int Id { get; set; }

        public required string Email { get; set; }
    }

    // Not sealed, like ASP.NET Core Identity's entity types, so the walk cannot prove its elements safe by type.
    public class Passkey
    {
        public int Id { get; set; }
    }

    public sealed class AccountsContext(DbContextOptions<AccountsContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts { get; set; } = null!;

        public DbSet<Passkey> Passkeys { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Passkey>();
            modelBuilder.Entity<Account>().HasIndex(account => account.Email).IsUnique();
        }
    }
}
