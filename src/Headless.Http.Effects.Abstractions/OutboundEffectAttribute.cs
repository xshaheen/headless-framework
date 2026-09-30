// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Http.Effects;

/// <summary>
/// Declares the side-effect class of one outbound provider client and derives its resilience
/// pipeline from that declaration. Applied to the provider's options class; the provider setup
/// (hand-written or generated) reads it through <see cref="EffectResilience"/>.
/// </summary>
/// <remarks>
/// The declaration is per provider client, not per operation, because every provider surveyed
/// exposes one effect class per API: Paymob CashOut is uniformly unsafe (money movement with no
/// idempotency key on <c>/disburse</c>), SMS sends are uniformly unsafe, and captcha site
/// verifies are uniformly safe. Finer grain would live on the broker methods themselves and is
/// deliberately out of scope for this abstraction.
/// </remarks>
/// <param name="effect">The side-effect class of every operation on the client.</param>
/// <param name="idempotencyHeader">
/// The header the provider uses to deduplicate retries, attached with a stable key for the
/// duration of one logical call when <paramref name="effect"/> is
/// <see cref="OutboundEffect.Idempotent"/>. Ignored otherwise.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OutboundEffectAttribute(OutboundEffect effect, string? idempotencyHeader = null) : Attribute
{
    /// <summary>The declared side-effect class of the client's operations.</summary>
    public OutboundEffect Effect { get; } = effect;

    /// <summary>
    /// The provider's idempotency header name (for example Stripe's <c>Idempotency-Key</c>),
    /// or <see langword="null"/> when the effect is not <see cref="OutboundEffect.Idempotent"/>.
    /// </summary>
    public string? IdempotencyHeader { get; } =
        idempotencyHeader is null ? null : Argument.IsNotEmpty(idempotencyHeader);
}
