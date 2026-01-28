// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.IdentityErrors;

public sealed class ParamsIdentityError : IdentityError
{
    /// <summary>Auxiliary error parameter: For example, RequiredLength, etc.</summary>
    public IReadOnlyDictionary<string, object?>? Params { get; private init; }

    // From ErrorDescriptor
    public static ParamsIdentityError FromErrorDescriptor(ErrorDescriptor error)
    {
        return new()
        {
            Code = error.Code,
            Description = error.Description,
            Params = error.Params,
        };
    }
}
