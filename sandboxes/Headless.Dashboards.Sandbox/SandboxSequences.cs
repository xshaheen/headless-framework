// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;

namespace Headless.Dashboards.Sandbox;

/// <summary>The sequence policies the sandbox declares, and the fixture that issues numbers under them.</summary>
public static class SandboxSequences
{
    public const string Invoice = "invoice";
    public const string Order = "order";
    public const string Ticket = "ticket";
    public const string Shipment = "shipment";

    /// <summary>A name with no registered policy, so its counter numbers with the default policy.</summary>
    public const string Unregistered = "quote";

    private static readonly TimeZoneInfo _Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static void Configure(SequencesOptions options)
    {
        Argument.IsNotNull(options);

        options.Policies[Invoice] = new SequencePolicy
        {
            Mode = SequenceMode.GapFree,
            Reset = SequenceReset.Year,
            Format = "INV-{yyyy}-{seq:D6}",
            TimeZone = _Cairo,
        };
        options.Policies[Order] = new SequencePolicy
        {
            Mode = SequenceMode.Fast,
            Reset = SequenceReset.Month,
            Format = "ORD-{yyyy}{MM}-{seq:D5}",
        };
        options.Policies[Ticket] = new SequencePolicy { Mode = SequenceMode.Fast, Format = "T-{seq}" };
        options.Policies[Shipment] = new SequencePolicy { Mode = SequenceMode.Reported, Reset = SequenceReset.Month };
    }

    /// <summary>
    /// Issues numbers in the host scope and in the tenants <c>acme</c> and <c>globex</c>: a gap-free invoice with a
    /// closed previous-year partition, monthly orders, never-resetting tickets, a reported shipment count, and a
    /// counter under an unregistered name. Running it again takes the next numbers of the same counters.
    /// </summary>
    /// <param name="provider">Resolves the generator, the unit-of-work factory, the current tenant, and the database.</param>
    /// <param name="cancellationToken">A token to cancel the calls.</param>
    /// <returns>Each counter and the last number or value it took, one line each.</returns>
    public static async Task<IReadOnlyList<string>> SeedAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);

        var connectionString = provider.GetRequiredService<SandboxSequencesDatabase>().ConnectionString;

        var generator = provider.GetRequiredService<ISequenceGenerator>();
        var units = provider.GetRequiredService<IUnitOfWorkFactory>();
        var tenant = provider.GetRequiredService<ICurrentTenant>();
        var clock = provider.GetRequiredService<TimeProvider>();
        var lastYear = (TimeZoneInfo.ConvertTime(clock.GetUtcNow(), _Cairo).Year - 1).ToString(
            CultureInfo.InvariantCulture
        );
        List<string> created =
        [
            await _FastAsync(generator, "", Ticket, 4, cancellationToken).ConfigureAwait(false),
            await _FastAsync(generator, "", Order, 2, cancellationToken).ConfigureAwait(false),
        ];

        using (tenant.Change("acme"))
        {
            created.Add(
                await _InvoicesAsync(units, connectionString, "acme", 3, cancellationToken).ConfigureAwait(false)
            );
            // An explicit partition is stored as given, so last year's invoices show as a closed partition.
            created.Add(
                await _InvoicesAsync(units, connectionString, "acme", 41, cancellationToken, lastYear)
                    .ConfigureAwait(false)
            );
            created.Add(await _FastAsync(generator, "acme", Order, 5, cancellationToken).ConfigureAwait(false));
            created.Add(
                await _ReportAsync(units, connectionString, "acme", 120, cancellationToken).ConfigureAwait(false)
            );
        }

        using (tenant.Change("globex"))
        {
            created.Add(
                await _InvoicesAsync(units, connectionString, "globex", 1, cancellationToken).ConfigureAwait(false)
            );
            created.Add(await _FastAsync(generator, "globex", Order, 3, cancellationToken).ConfigureAwait(false));
            var quote = await generator
                .NextAsync(Unregistered, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            created.Add(string.Create(CultureInfo.InvariantCulture, $"globex {Unregistered} {quote} (default policy)"));
        }

        return created;
    }

    private static async Task<string> _FastAsync(
        ISequenceGenerator generator,
        string scope,
        string name,
        int count,
        CancellationToken cancellationToken
    )
    {
        SequenceNumber? last = null;

        for (var i = 0; i < count; i++)
        {
            last = await generator.NextNumberAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return _Line(scope, name, last!);
    }

    private static async Task<string> _InvoicesAsync(
        IUnitOfWorkFactory units,
        string connectionString,
        string scope,
        int count,
        CancellationToken cancellationToken,
        string? partition = null
    )
    {
        await using var connection = new SqliteConnection(connectionString);
        await using var unit = await units.BeginAsync(connection, cancellationToken).ConfigureAwait(false);
        string line = "";

        for (var i = 0; i < count; i++)
        {
            if (partition is null)
            {
                line = _Line(
                    scope,
                    Invoice,
                    await unit.Sequences.NextNumberAsync(Invoice, cancellationToken).ConfigureAwait(false)
                );
            }
            else
            {
                var value = await unit.Sequences.NextAsync(Invoice, partition, cancellationToken).ConfigureAwait(false);
                line = string.Create(CultureInfo.InvariantCulture, $"{scope} {Invoice} {partition} {value}");
            }
        }

        await unit.CompleteAsync(cancellationToken).ConfigureAwait(false);

        return line;
    }

    private static async Task<string> _ReportAsync(
        IUnitOfWorkFactory units,
        string connectionString,
        string scope,
        long value,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqliteConnection(connectionString);
        await using var unit = await units.BeginAsync(connection, cancellationToken).ConfigureAwait(false);
        var advance = await unit
            .Sequences.AdvanceToAsync(Shipment, value, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await unit.CompleteAsync(cancellationToken).ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{scope} {Shipment} reported {value} ({advance.Status})");
    }

    private static string _Line(string scope, string name, SequenceNumber number) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(scope.Length == 0 ? "host" : scope)} {name} {number.Partition ?? "-"} {number.Value} {number.Text}"
        );
}

/// <summary>The SQLite database the sandbox keeps its counters in; gap-free units open their own connection to it.</summary>
public sealed record SandboxSequencesDatabase(string ConnectionString);
