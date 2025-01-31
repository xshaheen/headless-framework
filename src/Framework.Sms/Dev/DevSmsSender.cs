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

        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(request.MessageId))
        {
            sb.Append("MessageId: ").AppendLine(request.MessageId);
        }

        sb.Append("To: ").AppendLine(request.Destinations.JoinAsString(", "));
        sb.Append("Text: ").AppendLine(request.Text);

        if (request.Properties is not null)
        {
            sb.Append("Properties: ").AppendLine(JsonSerializer.Serialize(request.Properties));
        }

        await File.AppendAllTextAsync(
            _filePath,
            $"{sb}{Environment.NewLine}--------------------{Environment.NewLine}",
            cancellationToken
        );

        return SendSingleSmsResponse.Succeeded();
    }
}
