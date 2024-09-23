// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Emails.Contracts;

namespace Framework.Emails.Dev;

public sealed class NoopEmailSender : IEmailSender
{
    public ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(SendSingleEmailResponse.Succeeded());
    }
}
