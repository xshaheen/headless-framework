// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// The validation step MVC and Minimal API argument validation share: decide which argument types have a registered
/// validator, run every validator of an argument, and gather the failures into one list, so both stacks answer the same
/// 422.
/// </summary>
internal static class ArgumentValidation
{
    private static readonly MethodInfo _ValidateMethod = typeof(ArgumentValidation).GetMethod(
        nameof(_ValidateAsync),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    /// <summary>
    /// Gets the validator for arguments of <paramref name="type"/>, or <see langword="null"/> when no
    /// <see cref="IValidator{T}"/> is registered for it. Call it once per parameter when an endpoint or action is
    /// planned, never per request.
    /// </summary>
    public static ArgumentValidator? For(Type type, IServiceProviderIsService isService)
    {
        if (type.IsByRef || type.IsPointer || type.ContainsGenericParameters)
        {
            return null;
        }

        // Closed once here so each request validates through the typed IValidator<T> contract instead of the
        // non-generic one a custom validator may not implement.
        return isService.IsService(typeof(IValidator<>).MakeGenericType(type))
            ? _ValidateMethod.MakeGenericMethod(type).CreateDelegate<ArgumentValidator>()
            : null;
    }

    /// <summary>
    /// Runs every validator against <paramref name="instance"/> and appends their failures to
    /// <paramref name="failures"/>, creating the list on the first failure.
    /// </summary>
    /// <returns><paramref name="failures"/>, or a new list when it was <see langword="null"/> and a validator failed.</returns>
    public static async ValueTask<List<ValidationFailure>?> CollectFailuresAsync<T>(
        IEnumerable<IValidator<T>> validators,
        T instance,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken
    )
    {
        // Sequential, one context per validator: a FluentValidation result wraps its context's failure list, so a
        // shared context would hand every validator's failures to every result (and race when run concurrently),
        // and validators commonly share scoped services such as a DbContext that are not safe for concurrent use.
        foreach (var validator in validators)
        {
            var result = await validator
                .ValidateAsync(new ValidationContext<T>(instance), cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsValid)
            {
                (failures ??= []).AddRange(result.Errors);
            }
        }

        return failures;
    }

    private static ValueTask<List<ValidationFailure>?> _ValidateAsync<T>(
        IServiceProvider services,
        object argument,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken
    )
    {
        return CollectFailuresAsync(services.GetServices<IValidator<T>>(), (T)argument, failures, cancellationToken);
    }
}

/// <summary>Validates one argument with every registered validator of its type, adding to the shared failure list.</summary>
internal delegate ValueTask<List<ValidationFailure>?> ArgumentValidator(
    IServiceProvider services,
    object argument,
    List<ValidationFailure>? failures,
    CancellationToken cancellationToken
);
