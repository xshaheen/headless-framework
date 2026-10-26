// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Options;

/// <summary>
/// Validates every action argument whose type has a registered <see cref="IValidator{T}"/>, answering one 422 that
/// lists the failures of all of them, the way <c>ValidateArguments()</c> does for a Minimal API endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The arguments to validate are chosen once per action, on its first request, from its parameters and the registered
/// services. An action with none goes straight to the next filter, so it pays one dictionary lookup and allocates
/// nothing. It runs after the <c>[ApiController]</c> binding check, so a value that could not be bound answers 400
/// before any validator sees it. On a controller without <c>[ApiController]</c>, a request whose binding failed is not
/// validated either: its arguments hold defaults the client never sent, so the action decides what to do with the model
/// state, as ASP.NET Core leaves it.
/// </para>
/// <para>
/// One instance serves one host: the plans are keyed by that host's action descriptors, so they live and die with it.
/// </para>
/// </remarks>
internal sealed class ArgumentValidationActionFilter : IAsyncActionFilter
{
    private readonly ConcurrentDictionary<ActionDescriptor, ValidatedArgument[]> _plans = new();

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid)
        {
            return next();
        }

        var plan = _plans.GetOrAdd(
            context.ActionDescriptor,
            static (descriptor, services) => _Plan(descriptor, services),
            context.HttpContext.RequestServices
        );

        return plan.Length == 0 ? next() : _ValidateAsync(context, next, plan);
    }

    private static async Task _ValidateAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next,
        ValidatedArgument[] plan
    )
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;
        List<ValidationFailure>? failures = null;

        foreach (var (name, validate) in plan)
        {
            // An optional argument the request left out has nothing to validate.
            if (context.ActionArguments.TryGetValue(name, out var argument) && argument is not null)
            {
                failures = await validate(services, argument, failures, cancellationToken).ConfigureAwait(false);
            }
        }

        if (failures is null)
        {
            await next().ConfigureAwait(false);

            return;
        }

        var problemDetailsCreator = services.GetRequiredService<IProblemDetailsCreator>();

        context.Result = new ObjectResult(problemDetailsCreator.UnprocessableEntity(failures.ToErrorDescriptors()))
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity,
        };
    }

    private static ValidatedArgument[] _Plan(ActionDescriptor descriptor, IServiceProvider services)
    {
        if (services.GetService<IServiceProviderIsService>() is not { } isService)
        {
            return [];
        }

        List<ValidatedArgument>? arguments = null;

        foreach (var parameter in descriptor.Parameters)
        {
            var source = parameter.BindingInfo?.BindingSource;

            if (source == BindingSource.Services || source == BindingSource.Special)
            {
                continue;
            }

            if (ArgumentValidation.For(parameter.ParameterType, isService) is { } validate)
            {
                (arguments ??= []).Add(new ValidatedArgument(parameter.Name, validate));
            }
        }

        return arguments is null ? [] : [.. arguments];
    }

    private readonly record struct ValidatedArgument(string Name, ArgumentValidator Validate);
}
