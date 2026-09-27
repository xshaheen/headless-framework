// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A gap-free number taken inside an EF-begun unit (<c>RunAsync(db, …)</c>) goes through the same relational
/// resource as a raw-ADO unit: it commits together with the entity carrying it, and a failed block returns it.
/// </summary>
[Collection<PostgreSqlSequencesFixture>]
public sealed class PostgreSqlSequencesEntityFrameworkTests(PostgreSqlSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_commit_the_number_with_the_units_writes_and_return_it_on_rollback()
    {
        var name = $"invoice-{Guid.NewGuid():N}";
        await fixture.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS invoice_probe (id bigserial PRIMARY KEY, name text NOT NULL, number bigint NOT NULL)",
            AbortToken
        );
        await using var provider = await _BuildProviderAsync(name);

        await _RunAsync(
            provider,
            async (db, unit, ct) =>
            {
                var number = await unit.Sequences.NextAsync(name, cancellationToken: ct);
                db.Invoices.Add(new InvoiceProbe { Name = name, Number = number });
                await db.SaveChangesAsync(ct);

                unit.IsRetryPrevented.Should().BeFalse("the owned block replays by taking the number again");
            }
        );

        (await _CountInvoicesAsync(name)).Should().Be(1);
        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);

        var failing = async () =>
            await _RunAsync(
                provider,
                async (db, unit, ct) =>
                {
                    var number = await unit.Sequences.NextAsync(name, cancellationToken: ct);
                    number.Should().Be(2);
                    db.Invoices.Add(new InvoiceProbe { Name = name, Number = number });
                    await db.SaveChangesAsync(ct);

                    throw new InvalidOperationException("business rule failed after numbering");
                }
            );

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("business rule failed*");
        (await _CountInvoicesAsync(name)).Should().Be(1, "the failed block's row rolled back");
        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);

        long reissued = 0;
        await _RunAsync(
            provider,
            async (db, unit, ct) =>
            {
                reissued = await unit.Sequences.NextAsync(name, cancellationToken: ct);
                db.Invoices.Add(new InvoiceProbe { Name = name, Number = reissued });
                await db.SaveChangesAsync(ct);
            }
        );

        reissued.Should().Be(2, "the rolled-back number is handed out again");
        (await _CountInvoicesAsync(name)).Should().Be(2);
    }

    private async Task<ServiceProvider> _BuildProviderAsync(string gapFreeName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<InvoiceDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddHeadlessSequences(setup =>
        {
            fixture.ConfigureProvider(setup);
            setup.Policy(gapFreeName, new SequencePolicy { Mode = SequenceMode.GapFree });
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    private static async Task _RunAsync(
        ServiceProvider provider,
        Func<InvoiceDbContext, IUnitOfWork, CancellationToken, Task> operation
    )
    {
        // A fresh scope, and so a fresh context, per unit: a failed block leaves its entity tracked.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        await factory.RunAsync(db, (unit, ct) => operation(db, unit, ct), cancellationToken: AbortToken);
    }

    private Task<int> _CountInvoicesAsync(string name)
    {
        return fixture.ScalarAsync("SELECT count(*) FROM invoice_probe WHERE name = @name", AbortToken, ("name", name));
    }

    public sealed class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : DbContext(options)
    {
        public DbSet<InvoiceProbe> Invoices => Set<InvoiceProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceProbe>(entity =>
            {
                entity.ToTable("invoice_probe");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.Name).HasColumnName("name");
                entity.Property(row => row.Number).HasColumnName("number");
            });
        }
    }

    public sealed class InvoiceProbe
    {
        public long Id { get; init; }

        public required string Name { get; init; }

        public long Number { get; init; }
    }
}
