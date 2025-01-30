// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Emails.Contracts;
using Framework.Serializer;

namespace Framework.Emails.Dev;

public sealed class DevEmailSender(string filePath) : IEmailSender
{
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var json = JsonSerializer.Serialize(request, JsonConstants.DefaultPrettyJsonOptions);
        await File.AppendAllTextAsync(_filePath, $"{json}{Environment.NewLine}", cancellationToken);

        return SendSingleEmailResponse.Succeeded();
    }
}
