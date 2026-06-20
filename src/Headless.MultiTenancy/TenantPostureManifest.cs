// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Shared, non-PII manifest describing tenant posture configured by Headless package seams.</summary>
/// <remarks>
/// This manifest is a diagnostic breadcrumb, not a security or enforcement boundary. The actual
/// tenant enforcement lives in the seam middleware/handlers (HTTP resolution, authorization,
/// messaging propagation, EF write guard). Recording a seam or runtime marker here only affects
/// startup diagnostics — it neither creates nor removes real enforcement.
/// </remarks>
[PublicAPI]
public sealed class TenantPostureManifest
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TenantSeamPosture> _seams = new(StringComparer.Ordinal);

    /// <summary>Gets a snapshot of configured seam posture.</summary>
    public IReadOnlyCollection<TenantSeamPosture> Seams
    {
        get
        {
            lock (_gate)
            {
                return _seams.Values.ToArray();
            }
        }
    }

    /// <summary>Records or updates tenant posture for a seam.</summary>
    /// <param name="seam">The seam name.</param>
    /// <param name="status">The seam posture status.</param>
    /// <param name="capabilities">Optional non-PII capability labels.</param>
    /// <remarks>
    /// When an existing seam record exists, the resulting status is the strongest of the existing and
    /// incoming statuses per the precedence
    /// <c>Enforcing &gt; Guarded &gt; Propagating &gt; Configured</c>. This guarantees that a later
    /// <see cref="RecordSeam"/> call cannot weaken a posture already established by an earlier
    /// contribution — for example, a propagation-only contribution after a require-tenant enforcer
    /// must not downgrade the seam.
    /// </remarks>
    public void RecordSeam(string seam, TenantPostureStatus status, params string[] capabilities)
    {
        seam = Argument.IsNotNullOrWhiteSpace(seam);
        Argument.IsNotNull(capabilities);

        lock (_gate)
        {
            var normalizedCapabilities = _Normalize(capabilities);

            if (_seams.TryGetValue(seam, out var existing))
            {
                _seams[seam] = existing with
                {
                    Status = _MaxStatus(existing.Status, status),
                    Capabilities = _Merge(existing.Capabilities, normalizedCapabilities),
                };

                return;
            }

            _seams[seam] = new TenantSeamPosture(seam, status, normalizedCapabilities, []);
        }
    }

    private static TenantPostureStatus _MaxStatus(TenantPostureStatus left, TenantPostureStatus right)
    {
        // The enum ordinal IS the posture precedence (see TenantPostureStatus). Reject undefined
        // values — an out-of-range cast or a future member that bypassed this path — loudly instead
        // of silently down-ranking them to the weakest posture.
        _EnsureDefined(left);
        _EnsureDefined(right);

        return left >= right ? left : right;
    }

    private static void _EnsureDefined(TenantPostureStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                $"Unknown {nameof(TenantPostureStatus)} value."
            );
        }
    }

    /// <summary>Marks a runtime step, such as a middleware call, as applied for the seam.</summary>
    /// <param name="seam">The seam name.</param>
    /// <param name="marker">The runtime marker name.</param>
    /// <remarks>
    /// This records a non-PII diagnostic breadcrumb only; it is not an enforcement gate. Marking a
    /// runtime step that was not actually wired only fools the startup diagnostic — it does not
    /// enable the corresponding tenant behavior. Framework seam middleware is the intended caller.
    /// </remarks>
    public void MarkRuntimeApplied(string seam, string marker)
    {
        seam = Argument.IsNotNullOrWhiteSpace(seam);
        marker = Argument.IsNotNullOrWhiteSpace(marker);

        lock (_gate)
        {
            if (_seams.TryGetValue(seam, out var existing))
            {
                _seams[seam] = existing with { RuntimeMarkers = _Merge(existing.RuntimeMarkers, [marker]) };
                return;
            }

            _seams[seam] = new TenantSeamPosture(seam, TenantPostureStatus.Configured, [], [marker]);
        }
    }

    /// <summary>Gets the configured posture for a seam, if any.</summary>
    /// <param name="seam">The seam name.</param>
    /// <returns>The seam posture snapshot, or <see langword="null"/> when the seam is not configured.</returns>
    public TenantSeamPosture? GetSeam(string seam)
    {
        seam = Argument.IsNotNullOrWhiteSpace(seam);

        lock (_gate)
        {
            return _seams.GetValueOrDefault(seam);
        }
    }

    /// <summary>Returns whether a seam has any configured posture.</summary>
    /// <param name="seam">The seam name.</param>
    public bool IsConfigured(string seam) => GetSeam(seam) is not null;

    /// <summary>Returns whether the seam has a runtime marker.</summary>
    /// <param name="seam">The seam name.</param>
    /// <param name="marker">The runtime marker name.</param>
    public bool HasRuntimeMarker(string seam, string marker)
    {
        marker = Argument.IsNotNullOrWhiteSpace(marker);

        return GetSeam(seam)?.RuntimeMarkers.Contains(marker, StringComparer.Ordinal) == true;
    }

    private static string[] _Normalize(IEnumerable<string> values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] _Merge(IEnumerable<string> first, IEnumerable<string> second)
    {
        return first.Concat(second).Distinct(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Configured tenant posture for a single Headless seam.</summary>
/// <param name="Seam">The seam name.</param>
/// <param name="Status">The seam posture status.</param>
/// <param name="Capabilities">Non-PII capability labels reported by the seam.</param>
/// <param name="RuntimeMarkers">Non-PII runtime markers reported by the seam.</param>
[PublicAPI]
public sealed record TenantSeamPosture(
    string Seam,
    TenantPostureStatus Status,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RuntimeMarkers
);

/// <summary>Common tenant posture status labels, ordered weakest to strongest.</summary>
/// <remarks>
/// Declaration order is load-bearing: the ordinal IS the posture precedence
/// (<c>Configured &lt; Propagating &lt; Guarded &lt; Enforcing</c>), which
/// <see cref="TenantPostureManifest.RecordSeam"/> relies on so a later contribution can only
/// strengthen a seam's posture. Keep new members in precedence order.
/// </remarks>
[PublicAPI]
public enum TenantPostureStatus
{
    /// <summary>The seam has been configured.</summary>
    Configured = 0,

    /// <summary>The seam propagates tenant context.</summary>
    Propagating = 1,

    /// <summary>The seam guards tenant-owned writes.</summary>
    Guarded = 2,

    /// <summary>The seam enforces tenant context.</summary>
    Enforcing = 3,
}
