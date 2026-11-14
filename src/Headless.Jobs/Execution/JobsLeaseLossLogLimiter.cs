// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Jobs;

/// <summary>
/// Decides which lease losses log at <c>Warning</c>: the first for a function in each window, carrying the count of
/// the losses the window held back. A partition can take every running job of a function at once, and one line per
/// job would bury the rest of the log; the <c>lease_lost</c> outcome still counts each one.
/// </summary>
internal sealed class JobsLeaseLossLogLimiter(TimeProvider timeProvider, TimeSpan window)
{
    /// <summary>How long one function's lease-loss warning holds back the next.</summary>
    internal static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns <see langword="true" /> when this loss should log at <c>Warning</c>, with the losses of
    /// <paramref name="function" /> held back since its last warning in <paramref name="suppressed" />.
    /// </summary>
    /// <param name="function">
    /// The function key. Pass a bounded value, such as the metric's function tag, so the slots stay bounded.
    /// </param>
    /// <param name="suppressed">The losses held back since the last warning, when this one logs; otherwise 0.</param>
    public bool TryEnter(string function, out int suppressed)
    {
        var slot = _slots.GetOrAdd(function, static _ => new Slot());

        lock (slot)
        {
            if (slot.HasLogged && timeProvider.GetElapsedTime(slot.LastLoggedAt) < window)
            {
                slot.Suppressed++;
                suppressed = 0;

                return false;
            }

            slot.HasLogged = true;
            slot.LastLoggedAt = timeProvider.GetTimestamp();
            suppressed = slot.Suppressed;
            slot.Suppressed = 0;

            return true;
        }
    }

    private sealed class Slot
    {
        public bool HasLogged { get; set; }

        public long LastLoggedAt { get; set; }

        public int Suppressed { get; set; }
    }
}
