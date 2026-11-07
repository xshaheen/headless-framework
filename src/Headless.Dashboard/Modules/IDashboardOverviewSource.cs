// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>A module capability: up to three figures the shell shows for the module on the Overview page.</summary>
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

    /// <summary>Gets the value compared against.</summary>
    public required double Value { get; init; }

    /// <summary>
    /// Gets a value indicating whether a higher value than <see cref="Value"/> is the good direction, so the shell
    /// colors the change.
    /// </summary>
    public bool HigherIsBetter { get; init; }
}
