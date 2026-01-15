// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.IdentityErrors;

[PublicAPI]
public static class IdentityResultExtensions
{
    public static ParamsIdentityError ToIdentityError(this ErrorDescriptor error)
    {
        return ParamsIdentityError.FromErrorDescriptor(error);
    }

    public static ErrorDescriptor ToErrorDescriptor(this IdentityError error)
    {
        return error is ParamsIdentityError paramsError
            ? paramsError.ToErrorDescriptor()
            : new ErrorDescriptor(error.Code, error.Description);
    }

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

    public static Result<IReadOnlyList<ErrorDescriptor>> ToResult(this IdentityResult result)
    {
        return result.Succeeded
            ? Result<IReadOnlyList<ErrorDescriptor>>.Ok()
            : result.Errors.Select(error => error.ToErrorDescriptor()).ToList();
    }
}
