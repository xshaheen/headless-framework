// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.Benchmarks;

/// <summary>
/// Prints how many change-detection passes and database commands one save issues. Counts are deterministic, so they
/// are not a BenchmarkDotNet metric: run <c>--save-pipeline-probe</c> for them.
/// </summary>
internal static class SavePipelineProbe
{
    public static async Task RunAsync()
    {
        using (var host = new BenchmarkHost())
        {
            await using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

            for (var i = 0; i <= 1_000; i++)
            {
                db.Orders.Add(
                    new AuditedOrder
                    {
                        Id = Guid.NewGuid(),
                        CustomerName = string.Create(CultureInfo.InvariantCulture, $"c-{i}"),
                        Amount = i,
                    }
                );
            }

            await db.SaveChangesAsync().ConfigureAwait(false);
            var first = db.Orders.Local.First();

            host.Counter.Reset();
            first.Amount++;
            await db.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"1000 unchanged + 1 modified: DetectChanges={host.Counter.DetectChanges} SaveChanges={host.Counter.SaveChanges} Commands={host.Counter.Commands}"
                )
            );
        }

        foreach (var rows in new[] { 1, 10, 100 })
        {
            using var host = new BenchmarkHost();
            await using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

            for (var i = 0; i < rows; i++)
            {
                db.Orders.Add(
                    new AuditedOrder
                    {
                        Id = Guid.NewGuid(),
                        CustomerName = "c",
                        Amount = i,
                    }
                );
            }

            host.Counter.Reset();
            await db.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{rows} audited inserts: DetectChanges={host.Counter.DetectChanges} SaveChanges={host.Counter.SaveChanges} Commands={host.Counter.Commands}"
                )
            );
        }
    }
}
