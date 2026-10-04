// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Jobs.Instrumentation;

/// <summary>Describes the code that enqueued a job, for the enqueue log entry and trace tag.</summary>
/// <remarks>
/// Frames are described through <see cref="DiagnosticMethodInfo"/> rather than <see cref="StackFrame.GetMethod"/>,
/// which needs reflection metadata that trimming may remove. Where stack trace data is unavailable, such as a native
/// AOT app built without stack trace support, the caller is reported as <c>Unknown</c>.
/// </remarks>
internal static class CallerInfoHelper
{
    private const string _Unknown = "Unknown";

    /// <summary>
    /// Gets caller information by analyzing the stack trace
    /// </summary>
    /// <param name="skipFrames">Number of frames to skip from the current method</param>
    /// <returns>Formatted caller information</returns>
    public static string GetCallerInfo(int skipFrames = 4)
    {
        try
        {
            var stackTrace = new StackTrace(fNeedFileInfo: true);
            var frame = stackTrace.GetFrame(skipFrames);
            if (frame is null || DiagnosticMethodInfo.Create(frame) is not { } method)
            {
                return _Unknown;
            }

            var className = _SimpleTypeName(method.DeclaringTypeName) ?? _Unknown;
            var methodName = method.Name;
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();

            if (_IsCompilerGenerated(methodName))
            {
                // Async and iterator bodies run in generated methods; the next frame names the user method.
                var nextFrame = stackTrace.GetFrame(skipFrames + 1);
                if (nextFrame is not null && DiagnosticMethodInfo.Create(nextFrame) is { } nextMethod)
                {
                    className = _SimpleTypeName(nextMethod.DeclaringTypeName) ?? className;
                    methodName = nextMethod.Name;
                    fileName = nextFrame.GetFileName() ?? fileName;
                    lineNumber = nextFrame.GetFileLineNumber();
                }
            }

            if (!string.IsNullOrEmpty(fileName))
            {
                var shortFileName = Path.GetFileName(fileName);
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{className}.{methodName} ({shortFileName}:{lineNumber})"
                );
            }

            return $"{className}.{methodName}";
        }
#pragma warning disable ERP022 // Telemetry code must never crash the caller - returning "Unknown" is the safe fallback.
        catch
        {
            return _Unknown;
        }
#pragma warning restore ERP022
    }

    /// <summary>
    /// Gets a simple caller name without file information
    /// </summary>
    /// <param name="skipFrames">Number of frames to skip from the current method</param>
    /// <returns>Simple caller name</returns>
    public static string GetSimpleCallerInfo(int skipFrames = 4)
    {
        try
        {
            var stackTrace = new StackTrace(fNeedFileInfo: false);
            var frame = stackTrace.GetFrame(skipFrames);
            if (frame is null || DiagnosticMethodInfo.Create(frame) is not { } method)
            {
                return _Unknown;
            }

            var className = _SimpleTypeName(method.DeclaringTypeName) ?? _Unknown;
            var methodName = method.Name;

            if (_IsCompilerGenerated(methodName))
            {
                var nextFrame = stackTrace.GetFrame(skipFrames + 1);
                if (nextFrame is not null && DiagnosticMethodInfo.Create(nextFrame) is { } nextMethod)
                {
                    className = _SimpleTypeName(nextMethod.DeclaringTypeName) ?? className;
                    methodName = nextMethod.Name;
                }
            }

            return $"{className}.{methodName}";
        }
#pragma warning disable ERP022 // Telemetry code must never crash the caller - returning "Unknown" is the safe fallback.
        catch
        {
            return _Unknown;
        }
#pragma warning restore ERP022
    }

    private static bool _IsCompilerGenerated(string methodName) =>
        methodName.Contains('<', StringComparison.Ordinal) || methodName.Contains('>', StringComparison.Ordinal);

    /// <summary>
    /// The type name without namespace, containing types, or generic arguments, matching
    /// <see cref="System.Reflection.MemberInfo.Name"/>.
    /// </summary>
    private static string? _SimpleTypeName(string? fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
        {
            return null;
        }

        var genericArguments = fullTypeName.IndexOf('[', StringComparison.Ordinal);
        var name = genericArguments < 0 ? fullTypeName : fullTypeName[..genericArguments];
        return name[(name.LastIndexOfAny(['.', '+']) + 1)..];
    }
}
