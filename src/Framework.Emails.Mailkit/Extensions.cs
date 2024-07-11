using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Emails.Mailkit;

public static class Extensions
{
    public static void AddMailKitEmailSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(section);
        builder.Services.AddSingleton<IEmailSender, MailkitEmailSender>();
    }
}
