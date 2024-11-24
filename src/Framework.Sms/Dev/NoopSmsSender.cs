// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Dev;

public sealed class NoopSmsSender : ISmsSender
{
    public ValueTask<SendSingleSmsResponse> SendAsync(SendSingleSmsRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SendSingleSmsResponse.Succeeded());
    }
}
