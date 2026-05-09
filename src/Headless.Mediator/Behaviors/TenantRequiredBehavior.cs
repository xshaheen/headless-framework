// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Mediator;

namespace Headless.Mediator.Behaviors;

/// <summary>
/// Enforces that Mediator requests run with an ambient tenant context unless the request
/// type is marked with <see cref="AllowMissingTenantAttribute" />.
/// </summary>
/// <remarks>
/// Register <see cref="ICurrentTenant" /> separately before using this behavior. Missing
/// tenant failures throw <see cref="MissingTenantContextException" /> and add the
/// <c>Headless.Mediator.FailureCode</c> exception data value for layer-specific logging.
/// The exception default message carries the remediation guidance.
/// </remarks>
[PublicAPI]
public sealed class TenantRequiredBehavior<TRequest, TResponse>(ICurrentTenant currentTenant)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool _AllowMissingTenant = Attribute.IsDefined(
        typeof(TRequest),
        typeof(AllowMissingTenantAttribute),
        inherit: false
    );

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    public ValueTask<TResponse> Handle(
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken
    )
    {
        if (_AllowMissingTenant || !string.IsNullOrWhiteSpace(_currentTenant.Id))
        {
            return next.Invoke(message, cancellationToken);
        }

        var exception = new MissingTenantContextException();
        exception.Data["Headless.Mediator.FailureCode"] = "MissingTenantContext";

        throw exception;
    }
}
