// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.Sms.Testing;

/// <summary>
/// In-memory <see cref="ISmsSender"/> and <see cref="IBulkSmsSender"/> for tests: it records every request it accepts
/// instead of delivering it, so a test can assert what a user was sent. Register one instance as a singleton in place
/// of the default sender the application adds with <c>AddHeadlessSms</c>, then read <see cref="Sent"/> and
/// <see cref="SentBulk"/>.
/// </summary>
/// <remarks>
/// <para>
/// It implements both interfaces because every provider registers the default <see cref="IBulkSmsSender"/> as a cast of
/// the default <see cref="ISmsSender"/>, so a replacement that implemented only <see cref="ISmsSender"/> would make
/// <see cref="IBulkSmsSender"/> fail to resolve.
/// </para>
/// <para>
/// It validates a request as the development senders do: a <see langword="null"/> request, destination, or destination
/// list throws <see cref="ArgumentNullException"/>, and empty text or an empty destination list throws
/// <see cref="ArgumentException"/>. A rejected or cancelled request is not recorded. Every accepted request succeeds,
/// with no provider message identifier.
/// </para>
/// <para>
/// It is safe to share across threads: concurrent sends each record their request once, and <see cref="Sent"/> and
/// <see cref="SentBulk"/> return stable copies that later sends do not change.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RecordingSmsSender : ISmsSender, IBulkSmsSender
{
    private readonly ConcurrentQueue<SendSingleSmsRequest> _sent = new();
    private readonly ConcurrentQueue<SendBulkSmsRequest> _sentBulk = new();

    /// <summary>
    /// Gets a copy of the single-message requests sent through <see cref="SendAsync"/> so far, in the order the sends
    /// were accepted. The copy does not change when later sends arrive; read the property again to see them.
    /// </summary>
    public IReadOnlyList<SendSingleSmsRequest> Sent => [.. _sent];

    /// <summary>
    /// Gets a copy of the bulk requests sent through <see cref="SendBulkAsync"/> so far, in the order the sends were
    /// accepted. A bulk request is recorded once, here, and never in <see cref="Sent"/>.
    /// </summary>
    public IReadOnlyList<SendBulkSmsRequest> SentBulk => [.. _sentBulk];

    /// <summary>Discards every recorded single and bulk request, for example between two steps of one test.</summary>
    public void Clear()
    {
        _sent.Clear();
        _sentBulk.Clear();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or its <see cref="SendSingleSmsRequest.Destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">The request text is empty.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);
        cancellationToken.ThrowIfCancellationRequested();

        _sent.Enqueue(request);

        return ValueTask.FromResult(SendSingleSmsResponse.Succeeded());
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or its <see cref="SendBulkSmsRequest.Destinations"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">The destination list or the request text is empty.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);
        cancellationToken.ThrowIfCancellationRequested();

        _sentBulk.Enqueue(request);

        return ValueTask.FromResult(
            SendBulkSmsResponse.FromAggregate(request.Destinations, SendSingleSmsResponse.Succeeded())
        );
    }
}
