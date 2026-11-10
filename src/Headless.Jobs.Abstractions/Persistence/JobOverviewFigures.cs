// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Jobs;

/// <summary>The Overview figures: the last 24 hours, and the 24 hours before them for comparison.</summary>
/// <param name="Current">The window that ends at the read's instant.</param>
/// <param name="Previous">The window that ends where <paramref name="Current"/> starts.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobOverviewFigures(JobRunWindowFigures Current, JobRunWindowFigures Previous)
{
    /// <summary>The length of each window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
}
