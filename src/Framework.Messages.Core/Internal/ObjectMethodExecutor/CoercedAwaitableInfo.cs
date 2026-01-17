// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Linq.Expressions;

namespace Framework.Messages.Internal.ObjectMethodExecutor;

internal readonly struct CoercedAwaitableInfo(
    Expression coercerExpression,
    Type coercerResultType,
    AwaitableInfo coercedAwaitableInfo
)
{
    public AwaitableInfo AwaitableInfo { get; } = coercedAwaitableInfo;

    public Expression CoercerExpression { get; } = coercerExpression;

    public Type CoercerResultType { get; } = coercerResultType;

    public bool RequiresCoercion => CoercerExpression != null;

    public CoercedAwaitableInfo(AwaitableInfo awaitableInfo)
        : this(null, null, awaitableInfo) { }

    public static bool IsTypeAwaitable(Type type, out CoercedAwaitableInfo info)
    {
        if (AwaitableInfo.IsTypeAwaitable(type, out var directlyAwaitableInfo))
        {
            info = new CoercedAwaitableInfo(directlyAwaitableInfo);
            return true;
        }

        // It's not directly awaitable, but maybe we can coerce it.
        // Currently we support coercing FSharpAsync<T>.
        if (
            ObjectMethodExecutorFSharpSupport.TryBuildCoercerFromFSharpAsyncToAwaitable(
                type,
                out var coercerExpression,
                out var coercerResultType
            )
        )
        {
            if (AwaitableInfo.IsTypeAwaitable(coercerResultType, out var coercedAwaitableInfo))
            {
                info = new CoercedAwaitableInfo(coercerExpression, coercerResultType, coercedAwaitableInfo);
                return true;
            }
        }

        info = default;
        return false;
    }
}
