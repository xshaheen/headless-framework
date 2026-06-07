// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;

namespace Headless.Coordination;

[PublicAPI]
public sealed class CoordinationOptions
{
    public const string DefaultKeyPrefix = "coordination:";

    public const string DefaultClusterName = "default";

    /// <summary>
    /// DI key for the <c>IJsonSerializer</c> used to (de)serialize coordination metadata/endpoints. Consumers can
    /// pre-register their own keyed serializer under this key to override coordination's serialization independently
    /// of the global <c>IJsonSerializer</c>.
    /// </summary>
    public const string JsonSerializerServiceKey = "Headless:Coordination:JsonSerializer";

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

        RuleFor(x => x.DeadRetentionWindow)
            .Must((options, value) => value >= options.HeartbeatInterval * 2)
            .WithMessage("DeadRetentionWindow must be at least twice HeartbeatInterval.");
    }

    // Cluster names flow into Redis hash-tag keys and relational lock/identifier strings; restrict them to a
    // safe identifier set so quotes and control characters can never reach those surfaces.
    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClusterNameRegex { get;}
}
