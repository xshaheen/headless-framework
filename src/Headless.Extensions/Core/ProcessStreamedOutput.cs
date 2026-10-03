// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace System.Diagnostics;

/// <summary>A single item streamed by <see cref="HeadlessProcessExtensions.RunAndStreamAsync(ProcessStartInfo,CancellationToken)"/>: an output line or the final exit code.</summary>
/// <param name="Type">Whether this item is standard output, standard error, or the exit code.</param>
/// <param name="Text">The output line text, or the exit code rendered as a string.</param>
public sealed record ProcessStreamedOutput(ProcessStreamedOutputType Type, string Text)
{
    /// <summary>Returns the text, prefixed with <c>"error: "</c> for standard-error items.</summary>
    /// <returns>The formatted representation of this item.</returns>
    public override string ToString()
    {
        return Type switch
        {
            ProcessStreamedOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

/// <summary>Identifies the kind of item produced by <see cref="HeadlessProcessExtensions.RunAndStreamAsync(ProcessStartInfo,CancellationToken)"/>.</summary>
/// <remarks>Additional members may be added in future versions; consumers switching on this enum should include a default case.</remarks>
[PublicAPI]
public enum ProcessStreamedOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput = 0,

    /// <summary>A line written to standard error.</summary>
    StandardError = 1,

    /// <summary>The process exit code, reported once the process terminates.</summary>
    ExitCode = 2,
}
