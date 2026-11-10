// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Tests.Modules.Fixtures;

/// <summary>
/// A module whose UI build is one of the fixture folders embedded in this assembly. It declares one server command
/// and one related-object group, so overrides can name both.
/// </summary>
internal class FixtureModule(string id, string folder)
    : IDashboardModule,
        IDashboardCommandSource,
        IDashboardRelatedSource
{
    public string Id { get; } = id;

    public string DisplayName { get; } = id.ToUpperInvariant();

    public DashboardModuleAssets Assets { get; } =
        new() { Files = new EmbeddedFileProvider(typeof(FixtureModule).Assembly, $"Tests.Modules.Fixtures.{folder}") };

    public IReadOnlyList<DashboardCommand> Commands { get; } =
    [
        new DashboardCommand
        {
            Id = $"{id}.requeue",
            Label = "Requeue",
            ObjectKind = "run",
        },
    ];

    public IReadOnlyList<string> GroupIds { get; } = [$"{id}.run-related"];

    public void MapEndpoints(DashboardModuleEndpoints endpoints) { }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("The manifest merge never runs commands.");

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("The manifest merge never runs commands.");

    public Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        DashboardRelatedRequest request,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("The manifest merge never reads related objects.");
}

internal sealed class DiagModule() : FixtureModule("diag", "diag");

internal sealed class AuditModule() : FixtureModule("audit", "audit");

internal sealed class DiagModuleOptions : DashboardModuleOptions;

internal sealed class AuditModuleOptions : DashboardModuleOptions;
