using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Framework.Kernel.Checks;

public static partial class Ensure
{
    [Conditional("DEBUG")]
    [DebuggerStepThrough]
    public static void DebugAssert(
        [DoesNotReturnIf(false)] bool condition,
        string? detailMessage,
        [CallerArgumentExpression(nameof(condition))] string? expression = null
    )
    {
        Debug.Assert(condition, expression, detailMessage);
    }

    [Conditional("DEBUG")]
    [DebuggerStepThrough]
    [DoesNotReturn]
    public static void DebugFail(string message)
    {
        Debug.Fail(message);
    }
}
