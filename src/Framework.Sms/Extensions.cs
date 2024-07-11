using Framework.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms;

public static class Extensions
{
    public static void AddDevSmsSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISmsSender, DevSmsSender>();
    }

    public static void AddNoopSmsSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISmsSender, NoopSmsSender>();
    }
}
