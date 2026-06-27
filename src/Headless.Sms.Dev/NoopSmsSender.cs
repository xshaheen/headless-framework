// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms.Dev;

internal sealed class NoopSmsSender : ISmsSender, IBulkSmsSender
{
    public ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(SendSingleSmsResponse.Succeeded());
    }

    public ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);

        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            SendBulkSmsResponse.FromAggregate(request.Destinations, SendSingleSmsResponse.Succeeded())
        );
    }
}
