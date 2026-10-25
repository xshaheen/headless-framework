// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
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
/// The arguments to validate are chosen once per action, on its first request, from its parameters and the registered
/// services. An action with none goes straight to the next filter, so it pays one dictionary lookup and allocates
/// nothing. It runs after the <c>[ApiController]</c> binding check, so a value that could not be bound answers 400
/// before any validator sees it.
/// </remarks>
internal sealed class ArgumentValidationActionFilter : IAsyncActionFilter
{
    public static readonly ArgumentValidationActionFilter Instance = new();

    private static readonly MethodInfo _ValidateArgumentMethod = typeof(ArgumentValidationActionFilter).GetMethod(
        nameof(_ValidateArgumentAsync),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private readonly ConcurrentDictionary<ActionDescriptor, ValidatedArgument[]> _plans = new();

    private ArgumentValidationActionFilter() { }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
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
            var type = parameter.ParameterType;
            var source = parameter.BindingInfo?.BindingSource;

            if (
                type.IsByRef
                || type.IsPointer
                || type.ContainsGenericParameters
                || source == BindingSource.Services
                || source == BindingSource.Special
            )
            {
                continue;
            }

            if (isService.IsService(typeof(IValidator<>).MakeGenericType(type)))
            {
                // Closed once here so each request validates through the typed IValidator<T> contract, the same call
                // a Minimal API endpoint makes, instead of the non-generic one a custom validator may not implement.
                var validate = _ValidateArgumentMethod.MakeGenericMethod(type).CreateDelegate<ArgumentValidator>();
                (arguments ??= []).Add(new ValidatedArgument(parameter.Name, validate));
            }
        }

        return arguments is null ? [] : [.. arguments];
    }

    private static ValueTask<List<ValidationFailure>?> _ValidateArgumentAsync<T>(
        IServiceProvider services,
        object argument,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken
    )
    {
        return ArgumentValidation.CollectFailuresAsync(
            services.GetServices<IValidator<T>>(),
            (T)argument,
            failures,
            cancellationToken
        );
    }

    private delegate ValueTask<List<ValidationFailure>?> ArgumentValidator(
        IServiceProvider services,
        object argument,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken
    );

    private readonly record struct ValidatedArgument(string Name, ArgumentValidator Validate);
}
