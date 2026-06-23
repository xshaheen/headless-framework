// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Dev;

/// <summary>
/// No-op <see cref="IEmailSender"/> that silently discards every message.
/// </summary>
/// <remarks>
/// Useful in test environments or feature flags where email sending should be disabled
/// without changing application code. No I/O or network calls are made.
/// </remarks>
internal sealed class NoopEmailSender : IEmailSender
{
    /// <summary>
    /// Discards the email and returns a successful response immediately.
    /// </summary>
    /// <param name="request">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Always returns a successful <see cref="SendSingleEmailResponse"/>.</returns>
    public ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(SendSingleEmailResponse.Succeeded());
    }
}
