// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.IdentityErrors;

public static class IdentityResultExtensions
{
    public static ParamsIdentityError ToIdentityError(this ErrorDescriptor error)
    {
        return ParamsIdentityError.FromErrorDescriptor(error);
    }

    public static ErrorDescriptor ToErrorDescriptor(this IdentityError error)
    {
        return error is ParamsIdentityError paramsError
            ? ToErrorDescriptor(paramsError)
            : new ErrorDescriptor(error.Code, error.Description);
    }

    public static ErrorDescriptor ToErrorDescriptor(this ParamsIdentityError paramsError)
    {
        var descriptor = new ErrorDescriptor(paramsError.Code, paramsError.Description);

        foreach (var (key, value) in paramsError.Params)
        {
            descriptor.WithParam(key, value);
        }

        return descriptor;
    }

    public static NoDataResult ToResult(this IdentityResult result)
    {
        return result.Succeeded
            ? NoDataResult.Success()
            : result.Errors.Select(error => error.ToErrorDescriptor()).ToList();
    }
}
