// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

[PublicAPI]
public interface IResult<out TValue, out TError> : IResult<TError>
{
    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure);
}
