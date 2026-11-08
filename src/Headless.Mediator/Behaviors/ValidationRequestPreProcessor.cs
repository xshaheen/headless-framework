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
    ILogger<ValidationRequestPreProcessor<TMessage, TResponse>> logger,
    MediatorPayloadLogging payloadLogging
) : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly IEnumerable<IValidator<TMessage>> _validators = Argument.IsNotNull(validators);
    private readonly ILogger<ValidationRequestPreProcessor<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);
    private readonly bool _logPayloads = Argument.IsNotNull(payloadLogging).Enabled;

    /// <summary>
    /// Runs all registered validators for <typeparamref name="TMessage"/> and throws when any produce failures.
    /// </summary>
    /// <param name="message">The Mediator message to validate.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the validation.</param>
    /// <returns>A completed <see cref="ValueTask"/> when all validators pass.</returns>
    /// <exception cref="FluentValidation.ValidationException">
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
            // Fast path for the dominant single-validator case - avoids allocating the results array.
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
            // Sequential, one context per validator: validators commonly share scoped services such as a DbContext
            // that are not safe for concurrent use, and a shared context would hand every validator's failures to
            // every result.
            validationResults = new ValidationResult[validatorList.Count];

            for (var i = 0; i < validatorList.Count; i++)
            {
                validationResults[i] = await validatorList[i]
                    .ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)
                    .ConfigureAwait(false);
            }

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

        if (_logPayloads)
        {
            _LogWithPayload(_logger, typeof(TMessage).Name, message, failures, arg5: null);
        }
        else
        {
            _Log(_logger, typeof(TMessage).Name, failures, arg4: null);
        }

        throw new ValidationException(failures);
    }

    #region Logs

    // The failures stay in both shapes: each carries the property name and the validator's message, which is what a
    // caller needs to see why the message was rejected.
    private static readonly Action<ILogger, string, IReadOnlyCollection<ValidationFailure>, Exception?> _Log =
        LoggerMessage.Define<string, IReadOnlyCollection<ValidationFailure>>(
            LogLevel.Debug,
            new EventId(4, "Mediator:Validation"),
            "[Mediator:Validation] {MessageName} {Failures}"
        );

    private static readonly Action<
        ILogger,
        string,
        object,
        IReadOnlyCollection<ValidationFailure>,
        Exception?
    > _LogWithPayload = LoggerMessage.Define<string, object, IReadOnlyCollection<ValidationFailure>>(
        LogLevel.Debug,
        new EventId(4, "Mediator:Validation"),
        "[Mediator:Validation] {MessageName} {@Message} {Failures}"
    );

    #endregion
}
