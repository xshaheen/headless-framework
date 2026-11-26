// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.Emails.Testing;

/// <summary>
/// In-memory <see cref="IEmailSender"/> for tests: it records every request it accepts instead of delivering it, so a
/// test can assert what a user was sent. Register one instance as a singleton in place of the default sender the
/// application adds with <c>AddHeadlessEmails</c>, then read <see cref="Sent"/>.
/// </summary>
/// <remarks>
/// <para>
/// It validates a request as the real providers do: a <see langword="null"/> request throws
/// <see cref="ArgumentNullException"/>, and a request with neither body throws <see cref="InvalidOperationException"/>
/// from <see cref="SendSingleEmailRequest.EnsureHasBody"/>. A rejected or cancelled request is not recorded. Every
/// accepted request returns <see cref="SendSingleEmailResponse.Succeeded"/> with no provider message identifier.
/// </para>
/// <para>
/// It is safe to share across threads: concurrent sends each record their request once, and <see cref="Sent"/> returns
/// a stable copy that later sends do not change.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SendSingleEmailRequest> _sent = new();

    /// <summary>
    /// Gets a copy of the requests sent so far, in the order the sends were accepted. The copy does not change when
    /// later sends arrive; read the property again to see them.
    /// </summary>
    public IReadOnlyList<SendSingleEmailRequest> Sent => [.. _sent];

    /// <summary>Discards every recorded request, for example between two steps of one test.</summary>
    public void Clear() => _sent.Clear();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The request has neither an HTML nor a text body.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        request.EnsureHasBody();
        cancellationToken.ThrowIfCancellationRequested();

        _sent.Enqueue(request);

        return ValueTask.FromResult(SendSingleEmailResponse.Succeeded());
    }
}
