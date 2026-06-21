// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.IdentityErrors;

/// <summary>
/// Extension methods for converting between <see cref="IdentityError"/>/<see cref="IdentityResult"/>
/// and the framework's <see cref="ErrorDescriptor"/>/<see cref="Result{T}"/> types.
/// </summary>
[PublicAPI]
public static class IdentityResultExtensions
{
    /// <summary>Converts an <see cref="ErrorDescriptor"/> to a <see cref="ParamsIdentityError"/>.</summary>
    /// <param name="error">The descriptor to convert.</param>
    /// <returns>A <see cref="ParamsIdentityError"/> carrying the same code, description, and params.</returns>
    public static ParamsIdentityError ToIdentityError(this ErrorDescriptor error)
    {
        return ParamsIdentityError.FromErrorDescriptor(error);
    }

    /// <summary>
    /// Converts an <see cref="IdentityError"/> to an <see cref="ErrorDescriptor"/>.
    /// When the error is a <see cref="ParamsIdentityError"/>, structured params are preserved.
    /// </summary>
    /// <param name="error">The identity error to convert.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> with code and description from the identity error.</returns>
    public static ErrorDescriptor ToErrorDescriptor(this IdentityError error)
    {
        return error is ParamsIdentityError paramsError
            ? paramsError.ToErrorDescriptor()
            : new ErrorDescriptor(error.Code, error.Description);
    }

    /// <summary>
    /// Converts a <see cref="ParamsIdentityError"/> to an <see cref="ErrorDescriptor"/>,
    /// copying all structured params.
    /// </summary>
    /// <param name="paramsError">The params identity error to convert.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> with code, description, and all params.</returns>
    public static ErrorDescriptor ToErrorDescriptor(this ParamsIdentityError paramsError)
    {
        var descriptor = new ErrorDescriptor(paramsError.Code, paramsError.Description);

        if (paramsError.Params is null)
        {
            return descriptor;
        }

        foreach (var (key, value) in paramsError.Params)
        {
            descriptor.WithParam(key, value);
        }

        return descriptor;
    }

    /// <summary>
    /// Converts an <see cref="IdentityResult"/> to a <see cref="Result{T}"/> of error descriptors.
    /// </summary>
    /// <param name="result">The identity result to convert.</param>
    /// <returns>
    /// <see cref="Result{T}.Ok()"/> when <see cref="IdentityResult.Succeeded"/> is <see langword="true"/>;
    /// otherwise a failed result containing the converted <see cref="ErrorDescriptor"/> list.
    /// </returns>
    public static Result<IReadOnlyList<ErrorDescriptor>> ToResult(this IdentityResult result)
    {
        return result.Succeeded
            ? Result<IReadOnlyList<ErrorDescriptor>>.Ok()
            : result.Errors.Select(error => error.ToErrorDescriptor()).ToList();
    }
}
