using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms.VictoryLink;

public static class AddVictoryLinkExtensions
{
    public static void AddVictoryLinkSmsSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<VictoryLinkSettings, VictoryLinkSettingsValidator>(section);

        builder
            .Services.AddSingleton<ISmsSender, VictoryLinkSmsSender>()
            .AddHttpClient<ISmsSender, VictoryLinkSmsSender>(name: "victory-link-client")
            .AddStandardResilienceHandler();
    }
}
