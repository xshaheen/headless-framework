// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.IdentityErrors;

/// <summary>
/// An <see cref="IdentityError"/> that carries structured parameters (e.g. <c>MinLength</c>, <c>UniqueChars</c>)
/// alongside the code and description. Use <see cref="IdentityResultExtensions.ToErrorDescriptor(ParamsIdentityError)"/>
/// to convert back to an <see cref="ErrorDescriptor"/>.
/// </summary>
public sealed class ParamsIdentityError : IdentityError
{
    /// <summary>Auxiliary error parameters; for example, <c>MinLength</c>, <c>UniqueChars</c>.</summary>
    public IReadOnlyDictionary<string, object?>? Params { get; private init; }

    /// <summary>Creates a <see cref="ParamsIdentityError"/> from the given <see cref="ErrorDescriptor"/>.</summary>
    /// <param name="error">The source descriptor.</param>
    /// <returns>A new <see cref="ParamsIdentityError"/> with code, description, and params from <paramref name="error"/>.</returns>
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
