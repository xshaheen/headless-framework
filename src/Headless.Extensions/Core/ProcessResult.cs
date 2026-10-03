// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace System.Diagnostics;

/// <summary>The result of running a process to completion via <see cref="HeadlessProcessExtensions.RunAsTaskAsync(ProcessStartInfo,CancellationToken)"/>.</summary>
/// <param name="exitCode">The process exit code.</param>
/// <param name="output">The captured standard output and error lines, in the order they were received.</param>
public sealed class ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
{
    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>Gets the captured standard output and error lines.</summary>
    public ProcessOutputCollection Output { get; } = new(output);
}
