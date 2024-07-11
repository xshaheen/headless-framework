using Framework.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Emails;

public static class Extensions
{
    public static void AddDevEmailSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IEmailSender, DevEmailSender>();
    }

    public static void AddNoopEmailSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IEmailSender, NoopEmailSender>();
    }
}
