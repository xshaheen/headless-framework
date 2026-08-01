// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;

namespace Headless.Coordination;

internal sealed partial class CoordinationOptionsValidator : AbstractValidator<CoordinationOptions>
{
    public CoordinationOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();

        RuleFor(x => x.ClusterName)
            .NotEmpty()
            .Matches(ClusterNameRegex)
            .WithMessage("ClusterName may only contain letters, digits, '.', '_', ':', or '-'.");

        RuleFor(x => x.ConfiguredNodeId).Must(x => x is null || !string.IsNullOrWhiteSpace(x));
        RuleFor(x => x.HeartbeatInterval).GreaterThan(TimeSpan.Zero).LessThan(x => x.SuspicionThreshold);
        RuleFor(x => x.SuspicionThreshold).LessThan(x => x.DeadThreshold);
        RuleFor(x => x.DeadThreshold).GreaterThan(TimeSpan.Zero);

        // Strict ordering alone admits HeartbeatInterval=14s / DeadThreshold=16s — one dropped packet or GC
        // pause kills the node and triggers a cluster-wide reclaim of its work. Require the same style of
        // margin the DeadRetentionWindow rule below enforces: at least three beat attempts inside the window.
        RuleFor(x => x.DeadThreshold)
            .Must((options, value) => value >= options.HeartbeatInterval * 3)
            .WithMessage(
                "DeadThreshold must be at least three times HeartbeatInterval so a single missed or slow heartbeat cannot kill the node."
            );

        RuleFor(x => x.DeadRetentionWindow)
            .Must((options, value) => value >= options.HeartbeatInterval * 2)
            .WithMessage("DeadRetentionWindow must be at least twice HeartbeatInterval.");
    }

    // Cluster names flow into Redis hash-tag keys and relational lock/identifier strings; restrict them to a
    // safe identifier set so quotes and control characters can never reach those surfaces.
    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClusterNameRegex { get; }
}
