// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages using deterministic topic resolution, shared metadata defaults, and fail-fast validation.
/// </summary>
[PublicAPI]
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message using the configured topic mapping or an explicit topic override.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional publish overrides for topic, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the publish operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="MessagePublishOptionsBase.TenantId"/> is set to an empty or whitespace value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="MessagePublishOptionsBase.MessageId"/> exceeds <see cref="MessagePublishOptionsBase.MessageIdMaxLength"/>
    /// or <see cref="MessagePublishOptionsBase.TenantId"/> exceeds <see cref="MessagePublishOptionsBase.TenantIdMaxLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="MessagePublishOptionsBase.Headers"/> contains a reserved messaging header
    /// (use <see cref="MessagePublishOptionsBase"/> overrides instead), when a raw <see cref="Headers.TenantId"/>
    /// header is supplied without setting <see cref="MessagePublishOptionsBase.TenantId"/>, or when both are
    /// supplied with disagreeing values.
    /// </exception>
    Task PublishAsync<T>(T? contentObj, PublishOptions? options = null, CancellationToken cancellationToken = default);
}
