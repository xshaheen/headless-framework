// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;

namespace Headless.Api;

/// <summary>
/// The validation step MVC and Minimal API argument validation share: run every validator of an argument and gather
/// the failures into one list, so both stacks answer the same 422.
/// </summary>
internal static class ArgumentValidation
{
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
}
