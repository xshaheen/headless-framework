// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public interface IResult<out TError>
{
    /// <summary>Flag indicating whether if the operation succeeded or not.</summary>
    /// <value>True if the operation succeeded, otherwise false.</value>
    public bool Succeeded { get; }

    /// <summary>Flag indicating whether if the operation failed or not.</summary>
    /// <value>True if the operation failed, otherwise true.</value>
    public bool Failed { get; }

    public TResult Match<TResult>(Func<TResult> success, Func<TError, TResult> failure);
}
