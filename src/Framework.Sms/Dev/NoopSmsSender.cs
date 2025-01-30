// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Dev;

public sealed class NoopSmsSender : ISmsSender
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
