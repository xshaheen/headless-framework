// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Serilog;
using Serilog.Core;

namespace Framework.Sms.Dev;

public sealed class DevSmsSender : ISmsSender
{
    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        token.ThrowIfCancellationRequested();
        await using var logger = _CreateLogger(request.Destination.ToString());

        logger.Information("Sms: {@Request}", request);

        return SendSingleSmsResponse.Succeeded();
    }

    private static Logger _CreateLogger(string phoneNumber)
    {
        FormattableString template =
            $"Logs/Sms/{DateTimeOffset.Now:O}_{Guid.NewGuid().ToString("N")[..4]}_{phoneNumber}.txt";

        var filePath = template.ToString(CultureInfo.InvariantCulture);

        var logger = new LoggerConfiguration()
            .WriteTo.File(filePath, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        return logger;
    }
}
