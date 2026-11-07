// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Module;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> with no authentication and the Messaging module over
/// the in-memory store. Hosts built over one store share it as a deployment's nodes share one database.
/// </summary>
internal static class MessagingModuleTestHost
{
    public const string Api = "/headless/api/messaging";

    public static async Task<WebApplication> StartAsync(
        TimeProvider time,
        CancellationToken cancellationToken,
        string nodeName = "node-a",
        IDataStorage? sharedStorage = null,
        bool addMessaging = true,
        bool addJobs = false,
        Action<IServiceCollection>? configure = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(time);

        if (addMessaging)
        {
            builder.Services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
        }

        if (sharedStorage is not null)
        {
            builder.Services.AddSingleton(sharedStorage);
        }

        if (addJobs)
        {
            builder.Services.AddHeadlessJobs(jobs => jobs.DisableBackgroundServices());
        }

        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            dashboard.WithNoAuth();

            if (addJobs)
            {
                dashboard.AddJobs();
            }

            dashboard.AddMessaging(options => options.NodeName = nodeName);
        });

        configure?.Invoke(builder.Services);

        var app = builder.Build();

        try
        {
            app.MapHeadlessDashboard();
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    public static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    /// <summary>Reads stream events until a change whose id ends with <paramref name="storageId"/> arrives.</summary>
    public static async Task<JsonElement> NextChangeAsync(
        StreamReader reader,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        string? type = null;
        var data = new List<string>();
        var suffix = "~" + storageId.ToString("D");

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (string.Equals(type, "change", StringComparison.Ordinal) && data.Count > 0)
                {
                    using var document = JsonDocument.Parse(string.Join('\n', data));

                    if (document.RootElement.GetProperty("id").GetString()!.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        return document.RootElement.Clone();
                    }
                }

                (type, data) = (null, []);

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                type = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line["data:".Length..].TrimStart(' '));
            }
        }

        throw new InvalidOperationException("The stream ended before the change.");
    }
}

/// <summary>Writes the rows the module tests read straight into a store, as the pipelines would.</summary>
internal static class MessagingRows
{
    public const string Consumer = "billing.charge-consumer";
    public const string Name = "billing.charge";

    public static MediumMessage Medium(string messageId, string name = Name) =>
        new()
        {
            StorageId = Guid.Empty,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = messageId,
                    [Headers.MessageName] = name,
                },
                value: null
            ),
            Content = "{\"Headers\":{},\"Value\":{\"amount\":42}}",
            Lane = MessageLane.Bus,
        };

    /// <summary>Stores a received row and fails it for good with <paramref name="fingerprint"/>.</summary>
    public static async Task<MediumMessage> ReceivedFailureAsync(
        IDataStorage storage,
        string messageId,
        string fingerprint = "v1:card-declined",
        string consumer = Consumer,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await storage.StoreReceivedMessageAsync(Name, consumer, Medium(messageId), cancellationToken);
        stored.FailureFingerprint = fingerprint;
        (await storage.ChangeReceiveStateAsync(stored, StatusName.Failed, cancellationToken: cancellationToken))
            .Should()
            .BeTrue();

        return stored;
    }

    /// <summary>Stores a published row and moves it to <paramref name="status"/>.</summary>
    public static async Task<MediumMessage> PublishedAsync(
        IDataStorage storage,
        string messageId,
        StatusName status,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await storage.StoreMessageAsync(Name, Medium(messageId), cancellationToken: cancellationToken);

        if (status != StatusName.Scheduled)
        {
            (await storage.ChangePublishStateAsync(stored, status, cancellationToken: cancellationToken))
                .Should()
                .BeTrue();
        }

        return stored;
    }
}

/// <summary>Records every change published to this node's streams.</summary>
internal sealed class RecordingEventPublisher : IDashboardEventPublisher
{
    private readonly Lock _lock = new();
    private readonly List<DashboardChange> _changes = [];

    public IReadOnlyList<DashboardChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return [.. _changes];
            }
        }
    }

    public void Publish(DashboardChange change)
    {
        lock (_lock)
        {
            _changes.Add(change);
        }
    }
}
