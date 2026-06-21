// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Supplies the current node's owner identity for stamping durable job rows and for logging/telemetry.
/// </summary>
/// <remarks>
/// On the coordinated durable path the owner is the <c>node@incarnation</c> string allocated by the
/// membership substrate at host start; on the zero-infra in-memory path it falls back to the configured
/// node identifier. The two reads are intentionally split: <see cref="DisplayOwner"/> never throws and is
/// safe on the logging/telemetry hot path, while <see cref="TryGetStampOwner"/> is the authoritative gate
/// that refuses to yield an owner before registration completes or after membership is lost.
/// </remarks>
internal interface IJobsOwnerIdentity
{
    /// <summary>
    /// Best-effort owner label for logging and telemetry. Never throws. Returns the current
    /// <c>node@incarnation</c> when registered, otherwise a safe fallback (the configured node identifier).
    /// </summary>
    string DisplayOwner { get; }

    /// <summary>
    /// Authoritative stamp gate. Returns <see langword="false"/> when the node identity is not yet
    /// established (registration pending) or has been lost (local membership loss). Callers MUST NOT stamp
    /// durable rows when this returns <see langword="false"/>.
    /// </summary>
    bool TryGetStampOwner([NotNullWhen(true)] out string? owner);

    /// <summary>
    /// Signalled when local membership is lost so processing loops can fail-stop. Equals
    /// <see cref="CancellationToken.None"/> on the non-coordinated in-memory path (never fires).
    /// </summary>
    CancellationToken MembershipLostToken { get; }
}
