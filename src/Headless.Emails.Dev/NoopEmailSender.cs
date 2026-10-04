// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Dev;

/// <summary>
/// Discards every email message without sending network requests or saving content.
/// </summary>
/// <remarks>
/// Useful in test environments or behind feature flags where email sending must be disabled without
/// changing application code. No I/O or network calls are made.
/// </remarks>
internal sealed class NoopEmailSender : IEmailSender
{
    /// <summary>
    /// Discards the email message and returns a successful response.
    /// </summary>
    /// <param name="request">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Always a successful <see cref="SendSingleEmailResponse"/> instance.</returns>
    public ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(SendSingleEmailResponse.Succeeded());
    }
}
