using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms.Infobip;

public static class AddInfobipExtensions
{
    public static void AddInfobipSmsSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<InfobipSettings, InfobipSettingsValidator>(section);

        builder
            .Services.AddSingleton<ISmsSender, InfobipSmsSender>()
            .AddHttpClient<ISmsSender, InfobipSmsSender>(name: "infobip-client")
            .AddStandardResilienceHandler();
    }
}
