// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// Keeps the decorated controller or action out of idempotent response replay. Minimal API endpoints and route groups
/// apply it with <c>DisableResponseReplay()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Use it on an endpoint whose every call must run, such as one that audits each read, or whose response body must
/// never be stored, such as one that reveals personal data. Replay stores the response and serves it again without
/// running the handler, which such an endpoint cannot allow.
/// </para>
/// <para>
/// The middleware passes a request to such an endpoint straight through, as it does when
/// <see cref="HttpIdempotencyOptions.ShouldApply"/> returns <see langword="false"/>: an idempotency key the request
/// carries is ignored, neither validated, admitted, stored, nor replayed, and a request without a key is not refused
/// even when <see cref="HttpIdempotencyOptions.KeyRequired"/> is on. The handler sees no
/// <see cref="IHttpIdempotencyContext"/>.
/// </para>
/// <para>
/// It wins over <c>WithIdempotency(...)</c> and <c>RequireIdempotencyKey()</c> on the same endpoint, its controller,
/// its route group, or <c>MapControllers()</c>, whatever their order, and the endpoint documents no idempotency key or
/// rejections in its API description.
/// </para>
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class DisableResponseReplayAttribute : Attribute;
