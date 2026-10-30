// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;

#pragma warning disable CA1303 // Console report of a standalone scenario: not localized, and only a person reads it.

namespace Headless.Tus.Benchmarks;

/// <summary>
/// Sends each upload profile once through each store and prints the storage requests it cost, per upload and per
/// PATCH, with the per-operation breakdown. The counts are deterministic, so one upload is enough. Needs Docker:
/// <c>dotnet run -c Release --project benchmarks/Headless.Tus.Benchmarks -- requests</c>.
/// </summary>
internal static class StorageRequestScenario
{
    public static async Task RunAsync()
    {
        var payload = UploadProfiles.CreatePayload();
        await using var minio = await MinioServer.StartAsync();

        Console.WriteLine("| Store | Profile | PATCHes | Requests | Total / PATCHes | Seconds | Breakdown |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---|");

        foreach (var store in Enum.GetValues<TusStoreKind>())
        {
            await using var host = await TusUploadHost.StartAsync(store, minio);

            foreach (var profile in Enum.GetValues<UploadProfile>())
            {
                host.Counter.Reset();
                var clock = Stopwatch.StartNew();
                string? location = null;
                string? failure = null;

                try
                {
                    location = await host.UploadAsync(profile, payload);
                }
                catch (InvalidOperationException e)
                {
                    failure = e.Message;
                }

                clock.Stop();
                var counts = host.Counter.Snapshot();
                var total = counts.Values.Sum();
                var seconds = failure is null
                    ? clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)
                    : "failed";

                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {store} | {profile} | {profile.PatchCount()} | {total} | {total / (double)profile.PatchCount():0.##} | {seconds} | {StorageRequestCounter.Describe(counts)} |"
                    )
                );

                if (failure is not null)
                {
                    Console.WriteLine($"|  | {failure} |  |  |  |  |  |");
                }

                if (location is not null)
                {
                    await host.DeleteAsync(location);
                }
            }
        }
    }
}
