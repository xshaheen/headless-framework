using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms.Twilio;

public static class AddTwilioExtensions
{
    public static void AddTwilioSmsSender(this IHostApplicationBuilder builder, string configKey)
    {
        var section = builder.Configuration.GetRequiredSection(configKey);
        builder.Services.ConfigureSingleton<TwilioSettings, TwilioSettingsValidator>(section);

        builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();
    }
}
