// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Sms.Dev;

public sealed class DevSmsSender(string filePath) : ISmsSender
{
    private const string _Separator = "--------------------";
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

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

        sb.AppendLine(_Separator);

        await File.AppendAllTextAsync(_filePath, sb.ToString(), cancellationToken).AnyContext();

        return SendSingleSmsResponse.Succeeded();
    }
}
