// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public static class RouteBuilderExtensions
{
    public static RouteGroupBuilder AddExceptionFilter(this RouteGroupBuilder builder)
    {
        return builder.AddEndpointFilter<MinimalApiExceptionFilter>();
    }

    public static RouteHandlerBuilder Validate<TArgument>(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<MinimalApiValidatorFilter<TArgument>>();
    }
}
