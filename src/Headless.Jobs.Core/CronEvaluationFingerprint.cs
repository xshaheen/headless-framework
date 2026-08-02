// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NCrontab;

namespace Headless.Jobs;

/// <summary>
/// Fingerprints the rules a schedule position was derived under: the cron library's semantics and the effective
/// timezone's actual DST rules. Only equality is meaningful — a mismatch says "an identical expression and timezone
/// would now resolve to a different instant", which is a signal, not a value to interpret.
/// </summary>
/// <remarks>
/// This exists because an expression and a timezone id can stay byte-identical while the instant they resolve to
/// moves: a tzdata update shifts a zone's transition rules, or a cron library changes how it interprets a field.
/// Nothing in the persisted definition would record that, so a position derived under the old rules keeps a meaning
/// nobody can see has changed.
/// <para>
/// The zone's <see cref="TimeZoneInfo.GetAdjustmentRules"/> are hashed rather than a tzdata version string, for two
/// reasons: .NET exposes no portable tzdata version, and the rules are the thing that actually decides the instant.
/// It also makes a rule change testable by constructing a zone with different rules, instead of depending on whatever
/// tzdata the host happens to ship.
/// </para>
/// <para>
/// Deliberately excludes the cron expression. A changed expression already bumps <c>ScheduleRevision</c> and rebases
/// the projection through the edit path; this fingerprint covers only what changes <i>underneath</i> an unchanged
/// definition.
/// </para>
/// </remarks>
internal static class CronEvaluationFingerprint
{
    // The cron library's own version stands in for its parsing and traversal semantics: an upgrade that changes how a
    // field is interpreted ships as a new version, and nothing finer-grained is observable from outside the library.
    private static readonly string _CronLibraryVersion =
        typeof(CrontabSchedule).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Computes the fingerprint for the rules <paramref name="timeZone"/> currently defines.</summary>
    /// <param name="timeZone">The effective zone, already resolved from the definition's optional identifier.</param>
    /// <returns>A stable, opaque, 64-character lowercase hex digest.</returns>
    public static string Compute(TimeZoneInfo timeZone)
    {
        var builder = new StringBuilder(256);

        builder.Append("ncrontab=").Append(_CronLibraryVersion).Append('\n');
        builder.Append("zone=").Append(timeZone.Id).Append('\n');
        builder
            .Append("baseOffset=")
            .Append(timeZone.BaseUtcOffset.Ticks.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("supportsDst=").Append(timeZone.SupportsDaylightSavingTime ? '1' : '0').Append('\n');

        // Rules come back in chronological order, so the digest is stable without sorting. Every field that moves a
        // transition is included: a tzdata update that only shifts a transition time by an hour must still register.
        foreach (var rule in timeZone.GetAdjustmentRules())
        {
            builder
                .Append("rule=")
                .Append(rule.DateStart.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(rule.DateEnd.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(rule.DaylightDelta.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(_Transition(rule.DaylightTransitionStart))
                .Append('|')
                .Append(_Transition(rule.DaylightTransitionEnd))
                .Append('\n');
        }

        // SHA-256 rather than a runtime string hash: string.GetHashCode is randomized per process, so a fingerprint
        // built from it would differ after every restart and make every definition look stale on every boot.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexStringLower(digest);
    }

    private static string _Transition(TimeZoneInfo.TransitionTime transition)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(transition.IsFixedDateRule ? 1 : 0)}:{transition.Month}:{transition.Day}:{transition.Week}:{(int)transition.DayOfWeek}:{transition.TimeOfDay.TimeOfDay.Ticks}"
        );
    }
}
