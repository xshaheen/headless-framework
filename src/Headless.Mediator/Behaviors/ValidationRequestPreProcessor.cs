// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless.Checks;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Mediator.Behaviors;

/// <summary>
/// Runs all FluentValidation validators for a Mediator message before the handler executes.
/// </summary>
/// <remarks>
/// The pre-processor is transport-agnostic and throws <see cref="ValidationException" /> when
/// any registered <see cref="IValidator{T}" /> returns one or more failures. Registered as an open-generic
/// pipeline behavior by <see cref="Headless.Mediator.SetupMediator"/>; not intended for direct consumer use.
/// </remarks>
internal sealed class ValidationRequestPreProcessor<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators,
    ILogger<ValidationRequestPreProcessor<TMessage, TResponse>> logger
) : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly IEnumerable<IValidator<TMessage>> _validators = Argument.IsNotNull(validators);
    private readonly ILogger<ValidationRequestPreProcessor<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);

    /// <summary>
    /// Runs all registered validators for <typeparamref name="TMessage"/> and throws when any produce failures.
    /// </summary>
    /// <param name="message">The Mediator message to validate.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the validation.</param>
    /// <returns>A completed <see cref="ValueTask"/> when all validators pass.</returns>
    /// <exception cref="global::FluentValidation.ValidationException">
    /// Thrown when one or more validators return validation failures. All failures from all validators
    /// are aggregated into a single exception.
    /// </exception>
    protected override async ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        var validatorList = _validators as IList<IValidator<TMessage>> ?? [.. _validators];

        if (validatorList.Count == 0)
        {
            return;
        }

        ValidationResult[] validationResults;

        if (validatorList.Count == 1)
        {
            // Fast path for the dominant single-validator case - avoids Select/Task.WhenAll overhead.
            var result = await validatorList[0]
                .ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                return;
            }

            validationResults = [result];
        }
        else
        {
            validationResults = await Task.WhenAll(
                    validatorList.Select(validator =>
                        validator.ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)
                    )
                )
                .WithAggregatedExceptions();

            // Early exit if all valid - avoids the failure LINQ chain on the happy path.
            var allValid = true;

            foreach (var result in validationResults)
            {
                if (!result.IsValid)
                {
                    allValid = false;
                    break;
                }
            }

            if (allValid)
            {
                return;
            }
        }

        var failures = validationResults.SelectMany(result => result.Errors).ToList();

        _LogMediatorMessageValidation(
            _logger,
            messageName: typeof(TMessage).Name,
            message: message,
            failures: failures
        );

        throw new ValidationException(failures);
    }

    #region Logs

    private static readonly Action<ILogger, string, object, IReadOnlyCollection<ValidationFailure>, Exception?> _Log =
        LoggerMessage.Define<string, object, IReadOnlyCollection<ValidationFailure>>(
            LogLevel.Debug,
            new EventId(4, "Mediator:Validation"),
            "[Mediator:Validation] {MessageName} {@Message} {Failures}"
        );

    private static void _LogMediatorMessageValidation(
        ILogger logger,
        string messageName,
        object message,
        IReadOnlyCollection<ValidationFailure> failures
    )
    {
        _Log(logger, messageName, message, failures, arg5: null);
    }

    #endregion
}
