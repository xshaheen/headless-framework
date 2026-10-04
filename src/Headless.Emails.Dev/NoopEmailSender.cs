// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Dev;

/// <summary>
/// Discards every email message without sending network requests or saving content.
/// </summary>
internal sealed class NoopEmailSender : IEmailSender
{
    /// <summary>
    /// Discards the email message and returns a successful response.
    /// </summary>
    /// <param name="request">The email message request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A successful <see cref="SendSingleEmailResponse"/> instance.</returns>
    public ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(SendSingleEmailResponse.Succeeded());
    }
}
