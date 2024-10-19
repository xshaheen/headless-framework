// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;

namespace Framework.Api.Identity.IdentityErrors;

public static class ErrorDescriptorExtensions
{
    public static ParamsIdentityError ToIdentityError(this ErrorDescriptor error)
    {
        return ParamsIdentityError.FromErrorDescriptor(error);
    }
}
