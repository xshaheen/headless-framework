// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>
/// Delete one capture, and Clear a channel as an Operation, for each configured channel. Both go through the shell's
/// command path, so the operator log records them, and both publish the removal to every open view.
/// </summary>
/// <remarks>
/// Clear removes only the captures that existed when the operator confirmed it: its mark is the store's version at the
/// confirm, and a capture made later has a higher sequence. The stores restart their sequences with the process, so
/// the mark also names the store instance it was read from, and a mark from another process is refused.
/// </remarks>
internal sealed class DevInboxCommands(string moduleId, DevInboxChannels channels, IDashboardEventPublisher publisher)
{
    internal const string RedactedSearch = "(redacted)";

    private const char _MarkSeparator = ':';

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly DevInboxChannels _channels = Argument.IsNotNull(channels);
    private readonly IDashboardEventPublisher _publisher = Argument.IsNotNull(publisher);

    public static string DeleteId(string moduleId, string kind) => $"{moduleId}.{kind}.delete";

    public static string ClearId(string moduleId, string kind) => $"{moduleId}.{kind}.clear";

    public IReadOnlyList<DashboardCommand> Commands =>
        field ??= [
            .. _channels.All.SelectMany(channel =>
                new DashboardCommand[]
                {
                    new()
                    {
                        Id = DeleteId(_moduleId, channel.Kind),
                        Label = "Delete",
                        ObjectKind = channel.Kind,
                        Confirmation = DashboardConfirmation.Confirm,
                    },
                    new()
                    {
                        Id = ClearId(_moduleId, channel.Kind),
                        Label = "Clear",
                        ObjectKind = channel.Kind,
                        Confirmation = DashboardConfirmation.Confirm,
                        RequiresReason = true,
                    },
                }
            ),
        ];

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedAsync(IReadOnlyList<ObjectRef> targets)
    {
        var answers = new List<DashboardCommandAvailability>(targets.Count * 2);

        foreach (var target in targets)
        {
            if (
                !string.Equals(target.Module, _moduleId, StringComparison.Ordinal)
                || _channels.Find(target.Kind) is not { } channel
            )
            {
                continue;
            }

            var exists =
                DevInboxEndpoints.TryParseId(target.Id, out var id) && channel.Describe(_moduleId, id) is not null;

            answers.Add(
                new DashboardCommandAvailability
                {
                    Target = target,
                    CommandId = DeleteId(_moduleId, channel.Kind),
                    Allowed = exists,
                    DisabledReason = exists ? null : "This capture no longer exists.",
                }
            );
            answers.Add(
                new DashboardCommandAvailability
                {
                    Target = target,
                    CommandId = ClearId(_moduleId, channel.Kind),
                    Allowed = true,
                }
            );
        }

        return Task.FromResult<IReadOnlyList<DashboardCommandAvailability>>(answers);
    }

    public Task<DashboardCommandResult> ExecuteAsync(DashboardCommandRequest request)
    {
        var (channel, isClear) = _Command(request.CommandId);

        if (isClear)
        {
            return Task.FromResult(
                new DashboardCommandResult
                {
                    Rows = [],
                    Refusal = "Clear runs over the whole channel: confirm it as an Operation.",
                }
            );
        }

        var rows = new List<DashboardRowResult>(request.Ids.Count);

        foreach (var id in request.Ids)
        {
            rows.Add(_Delete(channel, id));
        }

        return Task.FromResult(new DashboardCommandResult { Rows = rows });
    }

    /// <summary>Counts and samples a Clear's scope and reads its mark.</summary>
    /// <exception cref="ArgumentException">The command is not a Clear, or the filter is not a channel filter.</exception>
    public Task<DashboardOperationScope> PrepareAsync(DashboardOperationScopeRequest request)
    {
        var channel = _ClearChannel(request.CommandId);
        var search = _ParseFilter(request.Filter);
        // Read the version first: a capture made after it has a higher sequence, so it stays out of the scope.
        var version = channel.Version;
        var scope = channel.Snapshot(search).Where(row => row.Sequence <= version).ToList();

        return Task.FromResult(
            new DashboardOperationScope
            {
                Count = scope.Count,
                Sample =
                [
                    .. scope
                        .Select(row => channel.Describe(_moduleId, row.Id))
                        .OfType<DashboardObject>()
                        .Take(request.SampleSize),
                ],
                HighWaterMark = channel.InstanceId + _MarkSeparator + version.ToString(CultureInfo.InvariantCulture),
                LoggedFilter = search is null
                    ? null
                    : JsonSerializer.SerializeToElement(
                        new DevInboxClearFilter(RedactedSearch),
                        DevInboxJsonContext.Default.DevInboxClearFilter
                    ),
            }
        );
    }

    /// <summary>
    /// Deletes the next chunk of a Clear's scope: the captures at or below its mark, and below the previous chunk's
    /// last sequence. The whole snapshot is filtered, so captures that arrive after the confirm never fill a chunk.
    /// </summary>
    /// <exception cref="ArgumentException">The mark is from another store instance, or the cursor is malformed.</exception>
    public Task<DashboardOperationChunk> RunChunkAsync(DashboardOperationChunkRequest request)
    {
        var channel = _ClearChannel(request.CommandId);
        var search = _ParseFilter(request.Filter);
        var mark = _ParseMark(channel, request.HighWaterMark);
        long? cursor = null;

        if (request.Cursor is { } value)
        {
            cursor = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"'{value}' is not a Dev inbox Clear cursor.", nameof(request));
        }

        var scope = channel
            .Snapshot(search)
            .Where(row => row.Sequence <= mark && (cursor is not { } below || row.Sequence < below))
            .ToList();
        var chunk = scope.Take(request.ChunkSize).ToList();
        var rows = chunk.ConvertAll(row => _Delete(channel, row.Id.ToString("D")));

        return Task.FromResult(
            new DashboardOperationChunk
            {
                Rows = rows,
                NextCursor =
                    scope.Count > chunk.Count ? chunk[^1].Sequence.ToString(CultureInfo.InvariantCulture) : null,
            }
        );
    }

    private DashboardRowResult _Delete(IDevInboxChannel channel, string id)
    {
        if (!DevInboxEndpoints.TryParseId(id, out var parsed) || !channel.Delete(parsed))
        {
            return new DashboardRowResult
            {
                Id = id,
                Outcome = DashboardRowOutcome.Skipped,
                Reason = "Already deleted.",
            };
        }

        // The store keeps no record of a deleted id, so the change poll cannot report it: publish the removal so every
        // open list drops the row and an open panel reads it as gone.
        _publisher.Publish(
            new DashboardChange
            {
                Target = new ObjectRef
                {
                    Module = _moduleId,
                    Kind = channel.Kind,
                    Id = parsed.ToString("D"),
                },
                Version = channel.Version.ToString(CultureInfo.InvariantCulture),
                Projection = null,
            }
        );

        return new DashboardRowResult { Id = id, Outcome = DashboardRowOutcome.Done };
    }

    private (IDevInboxChannel Channel, bool IsClear) _Command(string commandId)
    {
        foreach (var channel in _channels.All)
        {
            if (string.Equals(commandId, DeleteId(_moduleId, channel.Kind), StringComparison.Ordinal))
            {
                return (channel, false);
            }

            if (string.Equals(commandId, ClearId(_moduleId, channel.Kind), StringComparison.Ordinal))
            {
                return (channel, true);
            }
        }

        throw new ArgumentException($"'{commandId}' is not a Dev inbox command.", nameof(commandId));
    }

    private IDevInboxChannel _ClearChannel(string commandId)
    {
        var (channel, isClear) = _Command(commandId);

        return isClear
            ? channel
            : throw new ArgumentException($"Command '{commandId}' does not run over a filter.", nameof(commandId));
    }

    private static string? _ParseFilter(JsonElement filter)
    {
        if (filter.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The filter is not a Dev inbox channel filter.", nameof(filter));
        }

        if (!filter.TryGetProperty("search", out var search) || search.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return search.ValueKind == JsonValueKind.String
            ? search.GetString() is { Length: > 0 } text
                ? text
                : null
            : throw new ArgumentException("The filter's search must be text or null.", nameof(filter));
    }

    private static long _ParseMark(IDevInboxChannel channel, string mark)
    {
        var separator = mark.IndexOf(_MarkSeparator, StringComparison.Ordinal);

        if (
            separator <= 0
            || !long.TryParse(
                mark.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version
            )
        )
        {
            throw new ArgumentException($"'{mark}' is not a Dev inbox Clear mark.", nameof(mark));
        }

        return string.Equals(mark[..separator], channel.InstanceId, StringComparison.Ordinal)
            ? version
            : throw new ArgumentException(
                "This Clear was confirmed before the host restarted, and the captures it counted are gone. Confirm it again.",
                nameof(mark)
            );
    }
}

/// <summary>The filter of a Clear: the channel's search, or none for the whole channel.</summary>
internal sealed record DevInboxClearFilter(string? Search);
