// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Validates every handler argument whose parameter type has a registered <see cref="IValidator{T}"/>, as one
/// 422 response listing the failures of all of them.
/// </summary>
/// <remarks>
/// The arguments to validate are chosen once, when the endpoint is built, from the handler's parameters and the
/// registered services, so an endpoint with none gets no filter and pays nothing per request.
/// </remarks>
internal sealed class ArgumentValidationEndpointFilter : IEndpointFilter
{
    private readonly ValidatedArgument[] _arguments;

    private ArgumentValidationEndpointFilter(ValidatedArgument[] arguments)
    {
        _arguments = arguments;
    }

    public static void Apply(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault() is not { } method)
        {
            return;
        }

        if (endpoint.ApplicationServices.GetService<IServiceProviderIsService>() is not { } isService)
        {
            return;
        }

        var parameters = method.GetParameters();
        List<ValidatedArgument>? arguments = null;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (ArgumentValidation.For(parameters[i].ParameterType, isService) is { } validate)
            {
                (arguments ??= []).Add(new ValidatedArgument(i, validate));
            }
        }

        if (arguments is null)
        {
            return;
        }

        var filter = new ArgumentValidationEndpointFilter([.. arguments]);
        endpoint.FilterFactories.Add((_, next) => context => filter.InvokeAsync(context, next));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;
        List<ValidationFailure>? failures = null;

        foreach (var (index, validate) in _arguments)
        {
            // An optional argument the request left out has nothing to validate; a required one never reaches here.
            if (context.Arguments[index] is { } argument)
            {
                failures = await validate(services, argument, failures, cancellationToken).ConfigureAwait(false);
            }
        }

        return failures is null
            ? await next(context).ConfigureAwait(false)
            : EndpointValidation.UnprocessableEntity(context.HttpContext, failures);
    }

    private readonly record struct ValidatedArgument(int Index, ArgumentValidator Validate);
}
