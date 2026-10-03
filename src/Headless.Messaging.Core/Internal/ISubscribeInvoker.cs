// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

/// <summary>
/// Perform user definition method of consumers.
/// </summary>
internal interface ISubscribeInvoker
{
    /// <summary>
    /// Invoke subscribe method with the consumer context.
    /// </summary>
    /// <param name="context">consumer execute context</param>
    /// <param name="cancellationToken">The object of <see cref="CancellationToken" />.</param>
    Task<ConsumerExecutedResult> InvokeAsync(ConsumerContext context, CancellationToken cancellationToken = default);

    Task<ConsumerExecutedResult> InvokeInScopeAsync(
        ConsumerContext context,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    );
}
