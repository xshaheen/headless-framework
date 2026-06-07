// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination;

[PublicAPI]
public sealed class CoordinationOptions
{
    public const string DefaultKeyPrefix = "coordination:";

    public const string DefaultClusterName = "default";

    public string KeyPrefix { get; set; } = DefaultKeyPrefix;

    public string ClusterName { get; set; } = DefaultClusterName;

    public string? ConfiguredNodeId { get; set; }

    public string? Role { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan SuspicionThreshold { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan DeadThreshold { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan DeadRetentionWindow { get; set; } = TimeSpan.FromSeconds(30);

    public MembershipLostBehavior MembershipLostBehavior { get; set; } = MembershipLostBehavior.StopApplication;
}

internal sealed class CoordinationOptionsValidator : AbstractValidator<CoordinationOptions>
{
    public CoordinationOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.ClusterName).NotEmpty();
        RuleFor(x => x.ConfiguredNodeId).Must(x => x is null || !string.IsNullOrWhiteSpace(x));
        RuleFor(x => x.HeartbeatInterval).GreaterThan(TimeSpan.Zero).LessThan(x => x.SuspicionThreshold);
        RuleFor(x => x.SuspicionThreshold).LessThan(x => x.DeadThreshold);
        RuleFor(x => x.DeadThreshold).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.DeadRetentionWindow)
            .Must((options, value) => value >= options.HeartbeatInterval * 2)
            .WithMessage("DeadRetentionWindow must be at least twice HeartbeatInterval.");
    }
}
