// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.PushNotifications;
using Headless.Sms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>The capture stores the host registered; a store the host did not register is <see langword="null"/>.</summary>
internal sealed record DevInboxStores(
    IEmailCaptureStore? Email,
    ISmsCaptureStore? Sms,
    IPushNotificationCaptureStore? Push
)
{
    internal const string NotDevelopmentMessage =
        "The Dev inbox dashboard module (AddDevInbox) runs only in the Development environment, because it shows the "
        + "emails, SMS, and push notifications the host captured, which hold reset links and one-time codes. ";

    internal const string NoStoreMessage =
        "The Dev inbox dashboard module (AddDevInbox) found no capture store. Call UseDevelopment() on "
        + "AddHeadlessEmails, AddHeadlessSms, or AddHeadlessPushNotifications to capture emails, SMS, or push "
        + "notifications in memory.";

    /// <summary>
    /// Reads the host's environment and capture stores, and refuses a host that is not in Development or registered
    /// none of the stores.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The host is not in Development, has no <see cref="IHostEnvironment"/>, or registered no capture store.
    /// </exception>
    public static DevInboxStores Resolve(IServiceProvider services)
    {
        // An absent environment counts as not Development: a host that cannot say where it runs never shows captures.
        var environment =
            services.GetService<IHostEnvironment>()
            ?? throw new InvalidOperationException(
                NotDevelopmentMessage
                    + "This host registers no IHostEnvironment. Register AddDevInbox only when "
                    + "builder.Environment.IsDevelopment() is true."
            );

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                NotDevelopmentMessage
                    + $"This host runs in '{environment.EnvironmentName}'. Register AddDevInbox only when "
                    + "builder.Environment.IsDevelopment() is true."
            );
        }

        var stores = new DevInboxStores(
            services.GetService<IEmailCaptureStore>(),
            services.GetService<ISmsCaptureStore>(),
            services.GetService<IPushNotificationCaptureStore>()
        );

        return stores is { Email: null, Sms: null, Push: null }
            ? throw new InvalidOperationException(NoStoreMessage)
            : stores;
    }
}
