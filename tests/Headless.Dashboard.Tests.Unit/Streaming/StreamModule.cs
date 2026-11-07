// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Tests.Aggregation;

namespace Tests.Streaming;

/// <summary>
/// A module whose changes live in a shared <see cref="FakeChangeStore"/>, and whose payload formatter masks the
/// password <c>hunter2</c> in every <c>run</c> projection.
/// </summary>
internal abstract class StreamModule(string id, FakeChangeStore store)
    : IDashboardModule,
        IDashboardChangeSource,
        IDashboardPayloadFormatter
{
    public string Id { get; } = id;

    public string DisplayName => $"Module {Id}";

    public DashboardModuleAssets Assets =>
        new()
        {
            Files = new SingleFileProvider(
                "manifest.json",
                $$"""{"id":"{{Id}}","sdkVersion":"1.0.0","entry":"entry.js"}"""
            ),
        };

    public IReadOnlyList<string> ObjectKinds { get; } = ["run"];

    public void MapEndpoints(DashboardModuleEndpoints endpoints) { }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken) =>
        store.StartCursorAsync(cancellationToken);

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken) =>
        store.ReadAsync(Id, cursor);

    public ValueTask<DashboardPayload> FormatAsync(
        DashboardPayloadFormatRequest request,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(
            request.Payload with
            {
                Content = request.Payload.Content.Replace("hunter2", "***", StringComparison.Ordinal),
            }
        );
    }
}

internal sealed class DiagStreamModule(FakeChangeStore store) : StreamModule("diag", store);

internal sealed class DiagAStreamModule(FakeChangeStore store) : StreamModule("diag-a", store);

internal sealed class DiagBStreamModule(FakeChangeStore store) : StreamModule("diag-b", store);

internal sealed class DiagStreamModuleOptions : DashboardModuleOptions;

internal sealed class DiagAStreamModuleOptions : DashboardModuleOptions;

internal sealed class DiagBStreamModuleOptions : DashboardModuleOptions;
