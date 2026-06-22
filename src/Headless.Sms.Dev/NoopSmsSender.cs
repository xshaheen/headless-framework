// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Dev;

internal sealed class NoopSmsSender : ISmsSender
{
    public ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SendSingleSmsResponse.Succeeded());
    }
}
