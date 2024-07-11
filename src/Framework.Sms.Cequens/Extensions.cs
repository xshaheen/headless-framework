using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms.Cequens;

public static class Extensions
{
    public static void AddCequensSmsSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(section);

        builder
            .Services.AddSingleton<ISmsSender, CequensSmsSender>()
            .AddHttpClient<ISmsSender, CequensSmsSender>(name: "cequens-client")
            .AddStandardResilienceHandler();
    }
}
