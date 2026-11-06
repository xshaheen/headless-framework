// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: up to three figures the shell shows for the module on the Overview page, and the module's
/// tracks on the Overview's schedule strip.
/// </summary>
/// <remarks>
/// <para>
/// The shell calls every module's source in parallel with a per-module timeout; a source that times out or throws
/// shows as <c>Stale</c> in its place while the others still answer. The shell draws only the first three figures.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardOverviewSource
{
    /// <summary>Returns the module's Overview figures.</summary>
    /// <param name="request">The request: who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>At most three figures, in display order.</returns>
    Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Returns the module's tracks on the Overview's schedule strip: one per function, lane, or topic with timed work,
    /// holding the runs of the last hour and the fires due in the next hour. None by default.
    /// </summary>
    /// <param name="request">The request: who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The tracks, in display order.</returns>
    Task<IReadOnlyList<DashboardScheduleTrack>> GetScheduleAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyList<DashboardScheduleTrack>>([]);
    }
}

/// <summary>A request for a module's Overview figures.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardOverviewRequest
{
    /// <summary>Gets the signed-in user the figures are for.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>One Overview figure, such as "Failed runs, last hour: 12", with what it compares against.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardFigure
{
    /// <summary>Gets the figure's id, unique within the module.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the figure's label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the figure's value.</summary>
    public required double Value { get; init; }

    /// <summary>Gets the unit the shell prints after the value, such as <c>/min</c>, or <see langword="null"/>.</summary>
    public string? Unit { get; init; }

    /// <summary>Gets what the value compares against, or <see langword="null"/> when nothing does.</summary>
    public DashboardFigureComparison? Comparison { get; init; }
}

/// <summary>The value an Overview figure compares against, such as the same hour yesterday.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardFigureComparison
{
    /// <summary>Gets the comparison's label, such as <c>yesterday</c>.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the value compared against, or <see langword="null"/> when the earlier period has no value to compare,
    /// such as a success share over a period with no finished runs. The shell then shows <see cref="Label"/> alone, so
    /// it states why: <c>no runs finished in the previous 24 h</c>.
    /// </summary>
    public double? Value { get; init; }

    /// <summary>
    /// Gets a value indicating whether a higher value than <see cref="Value"/> is the good direction, so the shell
    /// colors the change.
    /// </summary>
    public bool HigherIsBetter { get; init; }
}

/// <summary>One track on the Overview's schedule strip, such as a function's runs and next fires.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardScheduleTrack
{
    /// <summary>Gets the track's id, unique within the module.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the track's label: the function, or the lane and topic, such as <c>billing.charge</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the runs and fires on the track; the shell draws the ones inside its window.</summary>
    public required IReadOnlyList<DashboardScheduleTick> Ticks { get; init; }
}

/// <summary>
/// One run or fire on a schedule track. A tick at or before now is a past run, drawn filled in its tone; a later one
/// is a fire, drawn hollow.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardScheduleTick
{
    /// <summary>Gets the tick's id, unique within the module.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the instant the module stored or computed, so a DST change shows as it happens.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets the status in words, for the tick's accessible name: <c>failed</c>, <c>next fire</c>.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the status tone: <c>healthy</c>, <c>active</c>, <c>needs</c>, <c>waiting</c>, or <c>stopped</c>;
    /// <see langword="null"/> for a fire, which has no status yet.
    /// </summary>
    public string? Tone { get; init; }

    /// <summary>Gets the object the tick opens when selected, or <see langword="null"/> when it opens nothing.</summary>
    public ObjectRef? Target { get; init; }
}
