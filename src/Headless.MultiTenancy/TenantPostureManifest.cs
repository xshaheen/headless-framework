// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Shared, non-PII manifest describing tenant posture configured by Headless package seams.</summary>
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
                    Status = status,
                    Capabilities = _Merge(existing.Capabilities, normalizedCapabilities),
                };

                return;
            }

            _seams[seam] = new TenantSeamPosture(seam, status, normalizedCapabilities, []);
        }
    }

    /// <summary>Marks a runtime step, such as a middleware call, as applied for the seam.</summary>
    /// <param name="seam">The seam name.</param>
    /// <param name="marker">The runtime marker name.</param>
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
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
public sealed record TenantSeamPosture(
    string Seam,
    TenantPostureStatus Status,
    IReadOnlyCollection<string> Capabilities,
    IReadOnlyCollection<string> RuntimeMarkers
);

/// <summary>Common tenant posture status labels.</summary>
public enum TenantPostureStatus
{
    /// <summary>The seam has been configured.</summary>
    Configured,

    /// <summary>The seam enforces tenant context.</summary>
    Enforcing,

    /// <summary>The seam propagates tenant context.</summary>
    Propagating,

    /// <summary>The seam guards tenant-owned writes.</summary>
    Guarded,
}
