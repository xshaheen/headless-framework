// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// evaluates against the host's own service-identity scheme. The key is never written to the manifest, logs, errors,
/// or responses.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class MessagingForwardingOptions
{
    /// <summary>The least length of <see cref="Key"/>.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>
    /// Gets or sets the key every node of the deployment shares, sent to a peer in the
    /// <c>X-Headless-Forwarding-Key</c> header and compared in fixed time. At least
    /// <see cref="MinimumKeyLength"/> characters. Leave it <see langword="null"/> when <see cref="Policy"/> is set.
    /// </summary>
    public string? Key { get; set; }

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
        // The message never quotes the key: validation failures reach logs and startup errors.
        RuleFor(x => x.Key)
            .Must(static key =>
                key is null
                || (key.Length >= MessagingForwardingOptions.MinimumKeyLength && !string.IsNullOrWhiteSpace(key))
            )
            .WithMessage(
                $"Forwarding.Key must be at least {MessagingForwardingOptions.MinimumKeyLength} characters, or null to turn the shared key off."
            );

        RuleFor(x => x.Policy)
            .Must(static policy => policy is null || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("Forwarding.Policy must name a host authorization policy, or be null.");

        RuleFor(x => x)
            .Must(static forwarding => forwarding.Key is null || forwarding.Policy is null)
            .WithMessage("Set Forwarding.Key or Forwarding.Policy, not both: every node admits its peers one way.");
    }
}
