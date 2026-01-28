// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

public interface ISmsSender
{
    ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
