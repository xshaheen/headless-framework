// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Settings of the Messaging module of the unified dashboard: the view and operate policies every dashboard module
/// carries, the name this node writes into object ids, and how the module reads its stores.
/// </summary>
[PublicAPI]
public sealed class MessagingDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Messaging module serves under: its URL segment, API group, and stream topic prefix.</summary>
    public const string ModuleId = "messaging";

    /// <summary>
    /// Gets or sets the name of this node in the ids of the objects it serves, <c>{node}~{store}~{storage id}</c>.
    /// Defaults to the machine name. Give every node of a deployment its own stable name, without <c>~</c> or white
    /// space, so an id copied from one node still names the node that served it.
    /// </summary>
    public string NodeName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Gets or sets how far back each change read of a store reads again, so a row whose transaction committed after
    /// a later one is still seen. Defaults to 5 seconds. It must exceed the longest transaction that enlists an outbox
    /// write: a row committed later than this after its write is not streamed, and shows on the next read of its list.
    /// </summary>
    public TimeSpan ChangeOverlapWindow { get; set; } = MessageChangeCursor.DefaultOverlapWindow;

    /// <summary>
    /// Gets or sets the most changed rows one change read returns from one store. Defaults to 200; a larger burst is
    /// read over several polls.
    /// </summary>
    public int ChangePageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets how long the module waits for one store in a read that covers several, such as the sidebar
    /// counts. A store that has not answered by then is reported unavailable by name while the others' answers are
    /// shown. Defaults to 1.5 seconds; keep it below the shell's <c>AggregationTimeout</c> (2 seconds by default),
    /// so one slow store does not mark the whole module stale.
    /// </summary>
    public TimeSpan StoreTimeout { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Gets or sets how this node calls the other Messaging nodes discovery lists, and how it admits their calls.
    /// Forwarding is off until a key or a policy is set.
    /// </summary>
    public MessagingForwardingOptions Forwarding { get; set; } = new();

    /// <summary>
    /// Gets or sets the most this node lets a call forwarded from another node do. Defaults to
    /// <see cref="MessagingForwardedAccess.View"/>: forwarded calls read, and every forwarded command is refused with
    /// "Node {name} accepts view-only forwarded calls". Set <see cref="MessagingForwardedAccess.Operate"/> to run the
    /// commands the serving node decided its operator may run.
    /// </summary>
    public MessagingForwardedAccess ForwardedAccess { get; set; } = MessagingForwardedAccess.View;
}

internal sealed class MessagingDashboardOptionsValidator : AbstractValidator<MessagingDashboardOptions>
{
    public MessagingDashboardOptionsValidator()
    {
        // A white-space policy name passes the shell's "empty means inherit" check but names no host policy, so
        // authorization would fail on every request instead of at startup.
        RuleFor(x => x.ViewPolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("ViewPolicy must name a host authorization policy, or be empty to use the shell's.");

        RuleFor(x => x.OperatePolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("OperatePolicy must name a host authorization policy, or be empty to use the view policy.");

        // The node name is the first part of every object id, and '~' separates the parts.
        RuleFor(x => x.NodeName)
            .Must(static name =>
                !string.IsNullOrEmpty(name)
                && !name.Any(static c => c == '~' || char.IsWhiteSpace(c) || char.IsControl(c))
            )
            .WithMessage("NodeName must be a non-empty name without '~', white space, or control characters.");

        RuleFor(x => x.ChangeOverlapWindow)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .WithMessage("ChangeOverlapWindow must not be negative.");

        RuleFor(x => x.ChangePageSize)
            .InclusiveBetween(1, 1000)
            .WithMessage("ChangePageSize must be between 1 and 1000.");

        RuleFor(x => x.StoreTimeout).GreaterThan(TimeSpan.Zero).WithMessage("StoreTimeout must be positive.");

        RuleFor(x => x.Forwarding).NotNull().SetValidator(new MessagingForwardingOptionsValidator());

        RuleFor(x => x.ForwardedAccess).IsInEnum();
    }
}
