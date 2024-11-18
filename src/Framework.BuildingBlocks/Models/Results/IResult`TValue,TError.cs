// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public interface IResult<out TValue, out TError> : IResult<TError>
{
    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure);
}
