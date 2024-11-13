// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Emails.Contracts;
using Serilog;
using Serilog.Core;

namespace Framework.Emails.Dev;

public sealed class DevEmailSender : IEmailSender
{
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        await using var logger = _CreateLogger();
        logger.Information("Request: {@Request}", request);

        return SendSingleEmailResponse.Succeeded();
    }

    private static Logger _CreateLogger()
    {
        FormattableString template = $"Logs/Emails/{DateTimeOffset.Now:O}-{Guid.NewGuid().ToString("N")[..4]}.html";
        var filePath = template.ToString(CultureInfo.InvariantCulture);

        var logger = new LoggerConfiguration()
            .WriteTo.File(filePath, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        return logger;
    }
}
