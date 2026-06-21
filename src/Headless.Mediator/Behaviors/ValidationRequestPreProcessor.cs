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
/// any registered <see cref="IValidator{T}" /> returns one or more failures.
/// </remarks>
[PublicAPI]
public sealed class ValidationRequestPreProcessor<TMessage, TResponse>(
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

        var validationResults = await Task.WhenAll(
                validatorList.Select(validator =>
                    validator.ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)
                )
            )
            .WithAggregatedExceptions();

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return;
        }

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
