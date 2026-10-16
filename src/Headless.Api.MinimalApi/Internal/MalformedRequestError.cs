// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api;

/// <summary>
/// Stands for a request that never reached the handler because the body was missing or did not bind, so the
/// dispatch helpers answer 400 the way the MVC controller helpers do.
/// </summary>
internal sealed record MalformedRequestError : ApiResultError
{
    public static readonly MalformedRequestError Instance = new();

    public override string Code => "g:bad_request";

    public override string Message => "The request is malformed.";
}
