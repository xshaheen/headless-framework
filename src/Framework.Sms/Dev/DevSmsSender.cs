// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Serializer;

namespace Framework.Sms.Dev;

public sealed class DevSmsSender(string filePath) : ISmsSender
{
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(request, JsonConstants.DefaultPrettyJsonOptions);
        await File.AppendAllTextAsync(_filePath, $"{json}{Environment.NewLine}", cancellationToken);

        return SendSingleSmsResponse.Succeeded();
    }
}
