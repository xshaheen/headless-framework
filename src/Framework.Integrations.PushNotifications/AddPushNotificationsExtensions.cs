using FirebaseAdmin;
using Framework.Integrations.PushNotifications.Dev;
using Framework.Integrations.PushNotifications.Gcm;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Integrations.PushNotifications;

public static class AddPushNotificationsExtensions
{
    public static void AddPushNotifications(this IHostApplicationBuilder builder, string sectionName)
    {
        var section = builder.Configuration.GetRequiredSection(sectionName);

        var settings =
            section.Get<FirebaseSettings>()
            ?? throw new InvalidOperationException($"{nameof(FirebaseSettings)} is not configured.");

        _LoadFirebase(settings.Json);
        builder.Services.AddSingleton<IPushNotificationService, GoogleCloudMessagingPushNotificationService>();
    }

    public static void AddNoopPushNotification(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
    }

    private static void _LoadFirebase(string json)
    {
        if (FirebaseApp.DefaultInstance is null)
        {
            FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(json), });
        }
    }
}
