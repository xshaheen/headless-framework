using FluentValidation;
using FluentValidation.Results;
using Framework.Api.Core.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Mediator;

public sealed class ApiValidationRequestPreProcessor<TMessage, TResponse> : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly IRequestContext _requestContext;
    private readonly IEnumerable<IValidator<TMessage>> _validators;
    private readonly ILogger<ApiValidationRequestPreProcessor<TMessage, TResponse>> _logger;

    public ApiValidationRequestPreProcessor(
        IRequestContext requestContext,
        IEnumerable<IValidator<TMessage>> validators,
        ILogger<ApiValidationRequestPreProcessor<TMessage, TResponse>> logger
    )
    {
        _requestContext = requestContext;
        _validators = validators;
        _logger = logger;
    }

    protected override async ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return;
        }

        var validationContext = new ValidationContext<TMessage>(message);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(validationContext, cancellationToken))
        );

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return;
        }

        _logger.LogMediatorMessageValidation(
            userId: _requestContext.User.UserId,
            messageName: typeof(TMessage).Name,
            message: message,
            failures: failures
        );

        throw new ValidationException(failures);
    }
}

file static class LoggerExtensions
{
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

    public static void LogMediatorMessageValidation(
        this ILogger logger,
        string? userId,
        string messageName,
        object message,
        IReadOnlyCollection<ValidationFailure> failures
    )
    {
        _Log(logger, userId, messageName, message, failures, null);
    }
}
