// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless;

/// <summary>
/// Computes a stable fingerprint that groups failures by cause, so a dashboard can show one item per cause instead of
/// one per first exception line, which often carries ids and values. Every feature that stores a failure fingerprint
/// computes it here, so fingerprints from different features stay comparable.
/// </summary>
/// <remarks>
/// <para>
/// Every fingerprint starts with the algorithm version (<see cref="VersionPrefix"/>). A change to the algorithm is a
/// new version: rows written under an older one keep their groups until they age out, instead of splitting a cause
/// into two groups that look unrelated.
/// </para>
/// <para>
/// <see cref="Compute"/> hashes the innermost exception type, after unwrapping <see cref="AggregateException"/> and
/// <see cref="TargetInvocationException"/>, plus the top three stack frames outside <c>System.*</c> and
/// <c>Microsoft.*</c>, each as a method full name with no line number and with compiler-generated lambda and async
/// state-machine names reduced to the declaring method. The result is the SHA-256 of those lines, truncated to 16 hex
/// characters. The message never takes part, because it carries the ids that would split one cause into many groups.
/// </para>
/// <para>
/// Compute the fingerprint where the exception is live, at the point that records the failure. Stored exception text
/// usually keeps only the outer message and one frame, so storage cannot derive the fingerprint later.
/// </para>
/// <para>
/// A failure with no exception to compute from (a sweep that ends a run whose node died) stores a fixed fingerprint
/// from <see cref="ForCause"/>, one per cause, so every store groups those failures alike.
/// </para>
/// </remarks>
[PublicAPI]
public static class FailureFingerprint
{
    /// <summary>The prefix every version-1 fingerprint starts with.</summary>
    public const string VersionPrefix = "v1:";

    private const int _FrameCount = 3;
    private const int _HexLength = 16;

    /// <summary>Returns the <c>v1:</c> fingerprint of <paramref name="exception"/>.</summary>
    /// <param name="exception">The live exception, with its stack trace; an exception never thrown hashes its type alone.</param>
    /// <returns><see cref="VersionPrefix"/> followed by 16 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
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

        return VersionPrefix + Convert.ToHexStringLower(hash.AsSpan(0, _HexLength / 2));
    }

    /// <summary>
    /// Returns the fixed <c>v1:</c> fingerprint for a failure that has no exception, such as <c>v1:lease-lapsed</c>.
    /// </summary>
    /// <param name="cause">
    /// The cause name: lowercase ASCII letters, digits, and hyphens. A 16-character name made only of hex digits is
    /// refused, because it could equal a fingerprint <see cref="Compute"/> returns.
    /// </param>
    /// <returns><see cref="VersionPrefix"/> followed by <paramref name="cause"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="cause"/> is empty or does not have the shape above.</exception>
    public static string ForCause(string cause)
    {
        Argument.IsNotNullOrEmpty(cause);
        Argument.IsTrue(
            _IsCauseName(cause),
            "A failure cause uses only lowercase ASCII letters, digits, and hyphens.",
            nameof(cause)
        );
        Argument.IsFalse(
            cause.Length == _HexLength && _IsLowerHex(cause),
            "A failure cause of 16 hex digits could equal a computed fingerprint.",
            nameof(cause)
        );

        return VersionPrefix + cause;
    }

    private static bool _IsCauseName(string cause)
    {
        foreach (var c in cause)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool _IsLowerHex(string value)
    {
        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'f') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
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
