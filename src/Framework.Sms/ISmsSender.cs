// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Sms;

public interface ISmsSender
{
    ValueTask<SendSingleSmsResponse> SendAsync(SendSingleSmsRequest request, CancellationToken token = default);
}
