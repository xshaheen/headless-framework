// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Emails.Mailkit;

public static class AddMailKitExtensions
{
    public static void AddMailKitEmailSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(section);
        builder.Services.AddSingleton<IEmailSender, MailkitEmailSender>();
    }

    public static void AddMailKitEmailSender(
        this IHostApplicationBuilder builder,
        Action<MailkitSmtpSettings> configure
    )
    {
        builder.Services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(configure);
        builder.Services.AddSingleton<IEmailSender, MailkitEmailSender>();
    }
}
