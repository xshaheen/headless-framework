// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms;

public interface ISmsSender
{
    ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
