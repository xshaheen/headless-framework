// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Jobs.Enums;

namespace Headless.Jobs.DashboardDtos;

/// <summary>
/// Per-status execution count for a single dashboard graph day: the number of executions that carry
/// <see cref="Status"/> on that day.
/// </summary>
/// <param name="Status">The lifecycle status counted by this entry.</param>
/// <param name="Count">Number of executions with <paramref name="Status"/> on the graph day.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobStatusCount(JobStatus Status, int Count);
