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
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(SendSingleSmsResponse.Succeeded());
    }

    public ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            SendBulkSmsResponse.FromAggregate(request.Destinations, SendSingleSmsResponse.Succeeded())
        );
    }
}
