// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using FluentValidation;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// How a Messaging node calls the other nodes service discovery lists, and how it admits their calls. Forwarding is
/// off until <see cref="Key"/> or <see cref="Policy"/> is set.
/// </summary>
/// <remarks>
/// <para>
/// A dashboard request for another node's object or list goes to that node, which reads its own stores and formats its
/// own payloads. The serving node sends the operator's name and its view or operate decision; the peer acts at the
/// lower of that decision and its own <see cref="MessagingDashboardOptions.ForwardedAccess"/>, and records the operator
/// as <c>{name} via {serving node}</c>.
/// </para>
/// <para>
/// Every node of a deployment sets the same mode: a shared <see cref="Key"/>, or a <see cref="Policy"/> each node
/// evaluates against the host's own service-identity scheme. In key mode a node never sends the key: it signs each
/// call with an HMAC-SHA256 over the call, and a peer admits it only inside <see cref="MaxClockSkew"/> and only once.
/// The key is never written to the manifest, logs, errors, or responses.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class MessagingForwardingOptions
{
    /// <summary>The least length of <see cref="Key"/>.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>The default of <see cref="MaxClockSkew"/>: 60 seconds.</summary>
    public static readonly TimeSpan DefaultMaxClockSkew = TimeSpan.FromSeconds(60);

    /// <summary>The largest <see cref="MaxClockSkew"/>: 10 minutes, so a captured call cannot be replayed for long.</summary>
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>The default of <see cref="MaxResponseBytes"/>: 4 MiB.</summary>
    public const int DefaultMaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the key every node of the deployment shares. A node signs each forwarded call with it, in the
    /// <c>X-Headless-Forwarding-Signature</c> header, and never sends the key itself. At least
    /// <see cref="MinimumKeyLength"/> characters. Leave it <see langword="null"/> when <see cref="Policy"/> is set.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets a second key this node admits signed calls under but never signs with, so the shared key can be
    /// rotated by rolling restarts without refusing a call: it holds the incoming key before the swap and the outgoing
    /// key after it. At least <see cref="MinimumKeyLength"/> characters, different from <see cref="Key"/>, and only
    /// valid with <see cref="Key"/> set. Remove it once every node signs with the new key.
    /// </summary>
    public string? PreviousKey { get; set; }

    /// <summary>
    /// Gets or sets how far a signed call's timestamp may be from this node's clock, either way, before the call is
    /// refused. A peer also refuses a second call with the same nonce inside this window. Defaults to
    /// <see cref="DefaultMaxClockSkew"/>; greater than zero and at most <see cref="MaximumClockSkew"/>. Keep the nodes'
    /// clocks synchronized well inside it.
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = DefaultMaxClockSkew;

    /// <summary>
    /// Gets or sets the name of a host authorization policy that admits a calling node, used instead of
    /// <see cref="Key"/>. The policy authenticates the node through the host's own service-identity scheme, such as a
    /// client certificate or a workload token, and must not be the dashboard's view or operate policy. The host
    /// configures the outbound client <c>Headless.Messaging.Dashboard.Forwarding</c> to present that identity.
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a peer that discovery lists with an <c>http://</c> address may be
    /// called. Defaults to <see langword="false"/>: such a peer gets no request, and shows as an insecure peer, because
    /// the forwarding credential and the operator's name would cross the network in clear text.
    /// </summary>
    public bool AllowInsecurePeers { get; set; }

    /// <summary>
    /// Gets or sets the largest answer, in bytes, this node reads from a peer. A larger answer is not read: the call
    /// fails as a peer that is not answering. Defaults to <see cref="DefaultMaxResponseBytes"/>; raise it when a
    /// message payload shown through forwarding can be larger.
    /// </summary>
    public int MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

    /// <summary>Gets a value indicating whether forwarding is on: a key or a policy is set.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Policy);
}

/// <summary>The most a peer lets a forwarded call do.</summary>
[PublicAPI]
public enum MessagingForwardedAccess
{
    /// <summary>Forwarded calls read; every forwarded command is refused.</summary>
    View = 0,

    /// <summary>Forwarded calls read and, when the serving node decided the operator may operate, run commands.</summary>
    Operate = 1,
}

internal sealed class MessagingForwardingOptionsValidator : AbstractValidator<MessagingForwardingOptions>
{
    public MessagingForwardingOptionsValidator()
    {
        // The messages never quote a key: validation failures reach logs and startup errors.
        RuleFor(x => x.Key)
            .Must(static key => key is null || _IsKey(key))
            .WithMessage(
                $"Forwarding.Key must be at least {MessagingForwardingOptions.MinimumKeyLength} characters, or null to turn the shared key off."
            );

        RuleFor(x => x.PreviousKey)
            .Must(static key => key is null || _IsKey(key))
            .WithMessage(
                $"Forwarding.PreviousKey must be at least {MessagingForwardingOptions.MinimumKeyLength} characters, or null."
            );

        RuleFor(x => x)
            .Must(static forwarding => forwarding.PreviousKey is null || forwarding.Key is not null)
            .WithMessage("Forwarding.PreviousKey needs Forwarding.Key: it only rotates the shared key.");

        RuleFor(x => x)
            .Must(static forwarding =>
                forwarding.PreviousKey is null
                || !string.Equals(forwarding.PreviousKey, forwarding.Key, StringComparison.Ordinal)
            )
            .WithMessage("Forwarding.PreviousKey must differ from Forwarding.Key.");

        RuleFor(x => x.MaxClockSkew)
            .Must(static skew => skew > TimeSpan.Zero && skew <= MessagingForwardingOptions.MaximumClockSkew)
            .WithMessage(
                $"Forwarding.MaxClockSkew must be greater than 0 and at most {MessagingForwardingOptions.MaximumClockSkew.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes."
            );

        RuleFor(x => x.Policy)
            .Must(static policy => policy is null || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("Forwarding.Policy must name a host authorization policy, or be null.");

        RuleFor(x => x.MaxResponseBytes)
            .GreaterThan(0)
            .WithMessage("Forwarding.MaxResponseBytes must be greater than 0.");

        RuleFor(x => x)
            .Must(static forwarding => forwarding.Key is null || forwarding.Policy is null)
            .WithMessage("Set Forwarding.Key or Forwarding.Policy, not both: every node admits its peers one way.");
    }

    private static bool _IsKey(string key) =>
        key.Length >= MessagingForwardingOptions.MinimumKeyLength && !string.IsNullOrWhiteSpace(key);
}
