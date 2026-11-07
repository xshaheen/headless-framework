// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// Computes the version-1 failure fingerprint of a live exception, as <see cref="JobFailureFingerprints"/> describes.
/// </summary>
/// <remarks>
/// The fingerprint is computed where the executor writes a failure, because the stored exception text keeps only the
/// outer message and one frame: storage cannot derive it later.
/// </remarks>
internal static class FailureFingerprint
{
    private const int _FrameCount = 3;
    private const int _HexLength = 16;

    /// <summary>Returns the <c>v1:</c> fingerprint of <paramref name="exception"/>.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "A frame whose method metadata was trimmed reads as null and is skipped, so a trimmed app still gets a stable fingerprint per build."
    )]
    public static string Compute(Exception exception)
    {
        Argument.IsNotNull(exception);

        var innermost = _Unwrap(exception);
        var input = new StringBuilder(innermost.GetType().FullName ?? innermost.GetType().Name);
        var taken = 0;

        foreach (var frame in new StackTrace(innermost, fNeedFileInfo: false).GetFrames())
        {
            if (taken == _FrameCount)
            {
                break;
            }

            if (frame.GetMethod() is not { } method || _MethodFullName(method) is not { } name)
            {
                continue;
            }

            input.Append('\n').Append(name);
            taken++;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString()));

        return JobFailureFingerprints.VersionPrefix + Convert.ToHexStringLower(hash)[.._HexLength];
    }

    // Only the two wrappers the runtime adds around a handler's own exception are unwrapped: an exception the handler
    // chose to wrap (a domain exception around an I/O fault) is the cause it reports.
    private static Exception _Unwrap(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case AggregateException { InnerExceptions.Count: > 0 } aggregate:
                    exception = aggregate.InnerExceptions[0];
                    break;
                case TargetInvocationException { InnerException: { } inner }:
                    exception = inner;
                    break;
                default:
                    return exception;
            }
        }
    }

    // A frame's method as Namespace.Type.Method, or null for a frame under System.* or Microsoft.*. A lambda, local
    // function, or async state machine lives on a compiler-generated type or method whose name carries the declaring
    // method in angle brackets (<Run>d__4, <Run>b__0_0, <<Run>b__0>d); both reduce to that declaring method.
    private static string? _MethodFullName(MethodBase method)
    {
        var type = method.DeclaringType;
        var name = method.Name;

        while (type is { IsNested: true } && _IsCompilerGenerated(type))
        {
            // An async or iterator state machine is named after the method it runs; a closure class (<>c,
            // <>c__DisplayClass0_0) is not, and its method's own name carries the declaring method.
            if (type.Name.StartsWith('<') && !type.Name.StartsWith("<>", StringComparison.Ordinal))
            {
                name = type.Name;
            }

            type = type.DeclaringType;
        }

        if (type is null)
        {
            return _Reduce(name);
        }

        if (_IsFrameworkNamespace(type.Namespace))
        {
            return null;
        }

        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var typeName = definition.FullName ?? definition.Name;

        return typeName + "." + _Reduce(name);
    }

    private static bool _IsCompilerGenerated(Type type) =>
        type.Name.StartsWith('<') || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool _IsFrameworkNamespace(string? ns) =>
        ns is not null
        && (
            ns.Equals("System", StringComparison.Ordinal)
            || ns.StartsWith("System.", StringComparison.Ordinal)
            || ns.Equals("Microsoft", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.", StringComparison.Ordinal)
        );

    // <<Run>b__0_0>d -> <Run>b__0_0 -> Run: the text inside the outermost angle brackets, until none remain.
    private static string _Reduce(string name)
    {
        while (name.StartsWith('<'))
        {
            var depth = 0;
            var close = -1;
            for (var i = 0; i < name.Length; i++)
            {
                if (name[i] == '<')
                {
                    depth++;
                }
                else if (name[i] == '>' && --depth == 0)
                {
                    close = i;
                    break;
                }
            }

            if (close <= 1)
            {
                return name;
            }

            name = name[1..close];
        }

        return name;
    }
}
