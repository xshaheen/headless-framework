// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Sends the module's work for another node's objects to that node: commands and Operations on its rows, Needs you,
/// id lookup, payloads, and, on a dashboard-only host, the sidebar counts and Overview of the node it opens on.
/// </summary>
/// <remarks>
/// A call that is itself forwarded is always served here: a peer never forwards again, so two nodes cannot loop.
/// A peer that does not answer is reported by its state; this node's own data never stands in for it.
/// </remarks>
internal sealed class MessagingForwarding(
    PeerDirectory directory,
    ForwardingClient client,
    MessagingDashboardOptions options,
    TimeProvider timeProvider,
    bool standalone
)
{
    /// <summary>How long one peer's Needs you answer is shown before the peer is asked again.</summary>
    internal static readonly TimeSpan AttentionLifetime = TimeSpan.FromSeconds(30);

    internal static readonly JsonSerializerOptions JsonOptions = CommandEndpoints.JsonOptions;

    private readonly ConcurrentDictionary<string, (DashboardAttentionAnswer Answer, DateTimeOffset At)> _attention =
        new(StringComparer.Ordinal);

    public PeerDirectory Directory => directory;

    public ForwardingClient Client => client;

    /// <summary>Gets a value indicating whether this host reads no store and serves every node through forwarding.</summary>
    public bool IsStandalone => standalone;

    public bool IsEnabled => directory.IsEnabled;

    /// <summary>Returns whether the request reached this node from another node.</summary>
    public static bool IsForwarded(ClaimsPrincipal? user) =>
        user?.HasClaim(static claim =>
            string.Equals(claim.Type, ForwardingProtocol.AdmittedClaim, StringComparison.Ordinal)
        ) == true;

    /// <summary>Returns whether a forwarded call may only read here.</summary>
    public static bool IsViewOnly(ClaimsPrincipal? user) =>
        IsForwarded(user) && !user!.HasClaim(ForwardingProtocol.AccessClaim, ForwardingProtocol.AccessOperate);

    /// <summary>Returns the node an object id names, or null when the id names none.</summary>
    public static string? NodeOf(string kind, string id)
    {
        return kind switch
        {
            MessagingViews.PublishedKind or MessagingViews.ReceivedKind => MessagingObjectId.TryParse(
                id,
                out var objectId
            )
                ? objectId.Node
                : null,
            MessagingViews.CircuitKind => MessagingViews.TryParseCircuitId(id, out var node, out _) ? node : null,
            MessagingViews.NodeKind => id,
            _ => null,
        };
    }

    /// <summary>Returns the peer a request should go to, or null when this node serves it.</summary>
    public async Task<MessagingPeer?> PeerForAsync(
        string? node,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled || IsForwarded(user) || node is null)
        {
            return null;
        }

        return await directory.FindAsync(node, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The node a dashboard-only host opens on: the first one discovery lists.</summary>
    public async Task<MessagingPeer?> DefaultPeerAsync(CancellationToken cancellationToken)
    {
        var peers = await directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);

        return peers.Count > 0 ? peers[0] : null;
    }

    public async Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        Func<DashboardCommandRequest, CancellationToken, Task<DashboardCommandResult>> local,
        CancellationToken cancellationToken
    )
    {
        var kind = MessagingCommands
            .All.FirstOrDefault(c => string.Equals(c.Id, request.CommandId, StringComparison.Ordinal))
            ?.ObjectKind;

        if (kind is null || !IsEnabled || IsForwarded(request.User))
        {
            return await local(request, cancellationToken).ConfigureAwait(false);
        }

        var groups = new Dictionary<string, (MessagingPeer? Peer, List<string> Ids)>(StringComparer.Ordinal);

        foreach (var id in request.Ids)
        {
            var peer = await directory.FindAsync(NodeOf(kind, id), cancellationToken).ConfigureAwait(false);
            var key = peer?.Name ?? string.Empty;

            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = (peer, []);
            }

            group.Ids.Add(id);
        }

        if (groups.Count == 1 && groups.ContainsKey(string.Empty))
        {
            return await local(request, cancellationToken).ConfigureAwait(false);
        }

        var rows = new Dictionary<string, DashboardRowResult>(StringComparer.Ordinal);

        foreach (var (_, (peer, ids)) in groups)
        {
            if (peer is null)
            {
                var answer = await local(request with { Ids = ids }, cancellationToken).ConfigureAwait(false);

                if (answer.Refusal is not null && request.Ids.Count == 1)
                {
                    return answer;
                }

                foreach (var row in answer.Rows)
                {
                    rows[row.Id] = row;
                }

                continue;
            }

            var result = await _RunOnPeerAsync(peer, request, ids, cancellationToken).ConfigureAwait(false);

            if (result.Refusal is not null && request.Ids.Count == 1)
            {
                return result;
            }

            foreach (var row in result.Rows)
            {
                rows[row.Id] = row;
            }
        }

        return new DashboardCommandResult
        {
            Rows =
            [
                .. request.Ids.Select(id =>
                    rows.TryGetValue(id, out var row)
                        ? row
                        : new DashboardRowResult { Id = id, Outcome = DashboardRowOutcome.Failed }
                ),
            ],
        };
    }

    public async Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedAsync(
        DashboardAllowedCommandsRequest request,
        Func<
            DashboardAllowedCommandsRequest,
            CancellationToken,
            Task<IReadOnlyList<DashboardCommandAvailability>>
        > local,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled || IsForwarded(request.User))
        {
            return await local(request, cancellationToken).ConfigureAwait(false);
        }

        var localTargets = new List<ObjectRef>();
        var remote = new Dictionary<string, (MessagingPeer Peer, List<ObjectRef> Targets)>(StringComparer.Ordinal);

        foreach (var target in request.Targets)
        {
            var peer = await directory
                .FindAsync(NodeOf(target.Kind, target.Id), cancellationToken)
                .ConfigureAwait(false);

            if (peer is null)
            {
                localTargets.Add(target);
            }
            else if (remote.TryGetValue(peer.Name, out var group))
            {
                group.Targets.Add(target);
            }
            else
            {
                remote[peer.Name] = (peer, [target]);
            }
        }

        var answers = new List<DashboardCommandAvailability>();

        if (localTargets.Count > 0)
        {
            answers.AddRange(
                await local(request with { Targets = localTargets }, cancellationToken).ConfigureAwait(false)
            );
        }

        foreach (var (_, (peer, targets)) in remote)
        {
            answers.AddRange(
                await _AllowedOnPeerAsync(peer, targets, request.User, cancellationToken).ConfigureAwait(false)
            );
        }

        return answers;
    }

    /// <summary>Returns the peer an Operation's filter names, or null when this node runs it.</summary>
    public async Task<MessagingPeer?> PeerOfFilterAsync(
        JsonElement filter,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var node =
            filter.ValueKind == JsonValueKind.Object
            && filter.TryGetProperty("node", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        var peer = await PeerForAsync(node, user, cancellationToken).ConfigureAwait(false);

        if (peer is null && standalone && node is null && !IsForwarded(user))
        {
            peer = await DefaultPeerAsync(cancellationToken).ConfigureAwait(false);
        }

        return peer;
    }

    public async Task<DashboardOperationScope> PrepareOnPeerAsync(
        MessagingPeer peer,
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new PeerScopeBody(request.Filter, request.CountLimit, request.SampleSize),
            JsonOptions
        );
        var response = await client
            .SendAsync(
                peer,
                HttpMethod.Post,
                "/peer/operations/" + Uri.EscapeDataString(request.CommandId) + "/scope",
                request.User,
                operate: true,
                body,
                cancellationToken
            )
            .ConfigureAwait(false);

        return _Read<DashboardOperationScope>(peer, response);
    }

    public async Task<DashboardOperationChunk> RunChunkOnPeerAsync(
        MessagingPeer peer,
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new PeerChunkBody(
                request.Filter,
                request.HighWaterMark,
                request.Cursor,
                request.ChunkSize,
                request.Arguments,
                request.Reason
            ),
            JsonOptions
        );
        var response = await client
            .SendAsync(
                peer,
                HttpMethod.Post,
                "/peer/operations/" + Uri.EscapeDataString(request.CommandId) + "/chunk",
                request.User,
                operate: true,
                body,
                cancellationToken
            )
            .ConfigureAwait(false);

        return _Read<DashboardOperationChunk>(peer, response);
    }

    /// <summary>
    /// Adds every peer's Needs you to this node's, each item labelled with its node, and an item for each peer whose
    /// last forwarded call failed.
    /// </summary>
    public async Task<DashboardAttentionAnswer> GetAttentionAsync(
        DashboardAttentionAnswer local,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
        {
            return local;
        }

        var peers = await directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);

        if (peers.Count == 0)
        {
            return local;
        }

        var items = new List<DashboardAttentionItem>(
            local.Items.Select(item => _Label(item, options.NodeName, peer: false))
        );
        var unavailable = new List<string>(local.UnavailableSources);
        var answers = await Task.WhenAll(peers.Select(peer => _PeerAttentionAsync(peer, cancellationToken)))
            .ConfigureAwait(false);

        for (var i = 0; i < peers.Count; i++)
        {
            var peer = peers[i];

            if (answers[i] is { } answer)
            {
                items.AddRange(answer.Items.Select(item => _Label(item, peer.Name, peer: true)));
            }

            if (directory.StatusOf(peer.Name) is { State: not (PeerState.Answering or PeerState.ViewOnly) } status)
            {
                items.Add(
                    new DashboardAttentionItem
                    {
                        Id = "node-state:" + peer.Name,
                        GroupKey = "node-state:" + peer.Name,
                        Title = status.State.Describe(peer.Name),
                        Severity = DashboardAttentionSeverity.Critical,
                        Count = 1,
                        LastSeenAt = status.At,
                        Target = MessagingViews.Ref(MessagingViews.NodeKind, peer.Name),
                    }
                );
            }
        }

        return new DashboardAttentionAnswer { Items = items, UnavailableSources = unavailable };
    }

    /// <summary>Adds every peer's matches for an id lookup to this node's.</summary>
    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        IReadOnlyList<DashboardObject> local,
        string query,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled || IsForwarded(user))
        {
            return local;
        }

        var peers = await directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);
        var answers = await Task.WhenAll(
                peers.Select(async peer =>
                {
                    var response = await client
                        .SendAsync(
                            peer,
                            HttpMethod.Get,
                            "/peer/resolve?q=" + Uri.EscapeDataString(query),
                            user,
                            operate: false,
                            json: null,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    return response.IsSuccess
                        ? JsonSerializer.Deserialize<List<DashboardObject>>(response.Body, JsonOptions) ?? []
                        : [];
                })
            )
            .ConfigureAwait(false);

        return [.. local, .. answers.SelectMany(static answer => answer)];
    }

    public async Task<DashboardPayload?> GetPayloadAsync(
        MessagingPeer peer,
        ObjectRef target,
        CancellationToken cancellationToken
    )
    {
        var response = await client
            .SendAsync(
                peer,
                HttpMethod.Get,
                "/peer/payload?kind=" + Uri.EscapeDataString(target.Kind) + "&id=" + Uri.EscapeDataString(target.Id),
                user: null,
                operate: false,
                json: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (response.Answered && response.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        return _Read<DashboardPayload>(peer, response);
    }

    /// <summary>Reads a dashboard-only host's sidebar counts or Overview from the node it opens on.</summary>
    public async Task<T?> ReadFromDefaultAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (await DefaultPeerAsync(cancellationToken).ConfigureAwait(false) is not { } peer)
        {
            return null;
        }

        var response = await client
            .SendAsync(peer, HttpMethod.Get, path, user: null, operate: false, json: null, cancellationToken)
            .ConfigureAwait(false);

        return response.IsSuccess ? JsonSerializer.Deserialize<T>(response.Body, JsonOptions) : null;
    }

    private async Task<DashboardAttentionAnswer?> _PeerAttentionAsync(
        MessagingPeer peer,
        CancellationToken cancellationToken
    )
    {
        var now = timeProvider.GetUtcNow();

        if (_attention.TryGetValue(peer.Name, out var cached) && now - cached.At < AttentionLifetime)
        {
            return cached.Answer;
        }

        var response = await client
            .SendAsync(
                peer,
                HttpMethod.Get,
                "/peer/attention",
                user: null,
                operate: false,
                json: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // The last answer stays visible while the peer is away; the node-state item says it is stale.
            return _attention.TryGetValue(peer.Name, out var last) ? last.Answer : null;
        }

        var answer = JsonSerializer.Deserialize<DashboardAttentionAnswer>(response.Body, JsonOptions);

        if (answer is not null)
        {
            _attention[peer.Name] = (answer, now);
        }

        return answer;
    }

    private static DashboardAttentionItem _Label(DashboardAttentionItem item, string node, bool peer)
    {
        return item with
        {
            Id = peer ? node + ":" + item.Id : item.Id,
            GroupKey = peer ? node + ":" + item.GroupKey : item.GroupKey,
            Title = item.Title + " (node " + node + ")",
            GroupFilter =
                peer && item.GroupFilter is { ValueKind: JsonValueKind.Object } filter
                    ? _WithNode(filter, node)
                    : item.GroupFilter,
        };
    }

    // The serving node keeps the group's filter and adds the node, so an Operation over the group runs on that node.
    private static JsonElement _WithNode(JsonElement filter, string node)
    {
        var json = JsonNode.Parse(filter.GetRawText())!.AsObject();
        json["node"] = node;

        return JsonSerializer.SerializeToElement(json, JsonOptions);
    }

    private async Task<DashboardCommandResult> _RunOnPeerAsync(
        MessagingPeer peer,
        DashboardCommandRequest request,
        List<string> ids,
        CancellationToken cancellationToken
    )
    {
        // The serving node's shell already checked the confirmation; the peer's shell checks it again on this body.
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new PeerCommandBody(ids, request.Arguments, request.Reason, Confirmed: true, TypedCount: ids.Count),
            JsonOptions
        );
        var response = await client
            .SendAsync(
                peer,
                HttpMethod.Post,
                "/commands/" + Uri.EscapeDataString(request.CommandId),
                request.User,
                operate: true,
                body,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (response.IsSuccess)
        {
            var rows = JsonSerializer.Deserialize<PeerRows>(response.Body, JsonOptions);

            return new DashboardCommandResult { Rows = rows?.Rows ?? [] };
        }

        var reason = response.Answered
            ? _Detail(response)
                ?? (
                    "Node "
                    + peer.Name
                    + " refused the command ("
                    + response.Status.ToString(CultureInfo.InvariantCulture)
                    + ")."
                )
            : response.State.Describe(peer.Name);

        if (response.Answered && response.Status == (int)HttpStatusCode.Conflict)
        {
            return new DashboardCommandResult
            {
                Rows =
                [
                    .. ids.Select(id => new DashboardRowResult
                    {
                        Id = id,
                        Outcome = DashboardRowOutcome.Skipped,
                        Reason = reason,
                    }),
                ],
                Refusal = reason,
            };
        }

        return new DashboardCommandResult
        {
            Rows =
            [
                .. ids.Select(id => new DashboardRowResult
                {
                    Id = id,
                    Outcome = DashboardRowOutcome.Failed,
                    Reason = reason,
                }),
            ],
            Refusal = ids.Count == 1 ? reason : null,
        };
    }

    private async Task<IReadOnlyList<DashboardCommandAvailability>> _AllowedOnPeerAsync(
        MessagingPeer peer,
        List<ObjectRef> targets,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var answers = new List<DashboardCommandAvailability>();

        foreach (var kind in targets.GroupBy(static target => target.Kind, StringComparer.Ordinal))
        {
            var query =
                "/peer/allowed?kind="
                + Uri.EscapeDataString(kind.Key)
                + string.Concat(kind.Select(static target => "&id=" + Uri.EscapeDataString(target.Id)));

            // The shell overrides each answer with this node's own refusal for a user who may not operate, so the
            // peer is asked as an operator: it answers what its rows and its own ceiling allow.
            var response = await client
                .SendAsync(peer, HttpMethod.Get, query, user, operate: true, json: null, cancellationToken)
                .ConfigureAwait(false);

            var peerAnswers = response.IsSuccess
                ? JsonSerializer.Deserialize<List<DashboardCommandAvailability>>(response.Body, JsonOptions)
                : null;

            if (peerAnswers is not null)
            {
                answers.AddRange(peerAnswers);
                continue;
            }

            var reason = response.State.Describe(peer.Name);

            foreach (var target in kind)
            {
                answers.AddRange(
                    MessagingCommands
                        .All.Where(command => string.Equals(command.ObjectKind, target.Kind, StringComparison.Ordinal))
                        .Select(command => new DashboardCommandAvailability
                        {
                            Target = target,
                            CommandId = command.Id,
                            Allowed = false,
                            DisabledReason = reason,
                        })
                );
            }
        }

        return answers;
    }

    private static T _Read<T>(MessagingPeer peer, ForwardResponse response)
        where T : class
    {
        if (response.IsSuccess && JsonSerializer.Deserialize<T>(response.Body, JsonOptions) is { } value)
        {
            return value;
        }

        if (response.Answered && response.Status == (int)HttpStatusCode.BadRequest)
        {
#pragma warning disable MA0015 // False positive: the peer refused the caller's request, not an argument of this method; the shell shows only the message.
            throw new ArgumentException(_Detail(response) ?? $"Node {peer.Name} refused the request as invalid.");
#pragma warning restore MA0015
        }

        throw new PeerUnavailableException(
            response.Answered
                ? $"Node {peer.Name} answered {response.Status.ToString(CultureInfo.InvariantCulture)}."
                : response.State.Describe(peer.Name)
        );
    }

    private static string? _Detail(ForwardResponse response)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Body);

            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                ? detail.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PeerRows(IReadOnlyList<DashboardRowResult> Rows);

    private sealed record PeerCommandBody(
        IReadOnlyList<string> Ids,
        IReadOnlyDictionary<string, JsonElement> Arguments,
        string? Reason,
        bool Confirmed,
        long TypedCount
    );
}

/// <summary>The body of a forwarded Operation scope read.</summary>
internal sealed record PeerScopeBody(JsonElement Filter, int CountLimit, int SampleSize);

/// <summary>The body of a forwarded Operation chunk.</summary>
internal sealed record PeerChunkBody(
    JsonElement Filter,
    string HighWaterMark,
    string? Cursor,
    int ChunkSize,
    IReadOnlyDictionary<string, JsonElement>? Arguments,
    string? Reason
);

/// <summary>A peer did not answer a forwarded call, or refused this node.</summary>
internal sealed class PeerUnavailableException(string message) : InvalidOperationException(message);
