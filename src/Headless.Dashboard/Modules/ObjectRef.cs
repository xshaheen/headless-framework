// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Identifies one object the dashboard shows, in any module: a job run, a received message, and so on.
/// </summary>
/// <remarks>
/// Modules pass references to each other's objects through the shell, for example to a
/// <see cref="IDashboardRelatedSource"/>, without referencing each other's packages. This record grows by
/// <see langword="init"/> members.
/// </remarks>
[PublicAPI]
public sealed record ObjectRef
{
    /// <summary>Gets the id of the module that owns the object.</summary>
    public required string Module { get; init; }

    /// <summary>Gets the object's kind within its module, such as <c>run</c> or <c>cron-job</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the object's id within its kind.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the node whose own store holds the object, or <see langword="null"/> when the module's store is shared by
    /// every node. A change to an object with a node is sent on the topic <c>{module}.{kind}@{node}</c> only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Node { get; init; }
}

/// <summary>
/// An object as the shell lists it in search results, related objects, and Operation samples: its reference and the
/// few fields every list shows.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardObject
{
    /// <summary>Gets the object's reference.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>Gets the object's title, such as a job's function name or a message's topic.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the object's current status as its module names it, such as <c>Failed</c>, or <see langword="null"/>.</summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the W3C trace id the object carries, or <see langword="null"/>. When set, the shell offers
    /// <c>Open trace</c> through the host's trace link template.
    /// </summary>
    public string? TraceId { get; init; }
}
