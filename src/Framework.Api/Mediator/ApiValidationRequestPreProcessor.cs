// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Framework.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Mediator;

[PublicAPI]
public sealed class ApiValidationRequestPreProcessor<TMessage, TResponse>(
    IRequestContext requestContext,
    IEnumerable<IValidator<TMessage>> validators,
    ILogger<ApiValidationRequestPreProcessor<TMessage, TResponse>> logger
) : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    protected override async ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return;
        }

        var validationContext = new ValidationContext<TMessage>(message);

        var validationResults = await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(validationContext, cancellationToken))
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
            logger,
            userId: requestContext.User.UserId,
            messageName: typeof(TMessage).Name,
            message: message,
            failures: failures
        );

        throw new ValidationException(failures);
    }

    #region Logs

    private static readonly Action<
        ILogger,
        string?,
        string,
        object,
        IReadOnlyCollection<ValidationFailure>,
        Exception?
    > _Log = LoggerMessage.Define<string?, string, object, IReadOnlyCollection<ValidationFailure>>(
        LogLevel.Debug,
        new EventId(4, "Mediator:Validation"),
        "[Mediator:Validation] {UserId} {MessageName} {@Message} {Failures}"
    );

    private static void _LogMediatorMessageValidation(
        ILogger logger,
        string? userId,
        string messageName,
        object message,
        IReadOnlyCollection<ValidationFailure> failures
    )
    {
        _Log(logger, userId, messageName, message, failures, arg6: null);
    }

    #endregion
}
