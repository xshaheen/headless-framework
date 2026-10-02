// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using var cts = new CancellationTokenSource();
var container = new ServiceCollection();

container.AddLogging(x => x.AddConsole());

container
    .AddHeadlessMessaging(setup =>
    {
        setup.AddModule<Headless.Messaging.Console.Demo.MessagingModule>();
        // Console app does not support dashboard
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
        setup.UseInMemory();
    })
    .AddBusConsumeMiddleware<CustomConsumerMiddleware>();

container.ConfigureMessaging(messaging =>
{
    messaging.Message<ShowTimeEvent>("sample.console.showtime");
    messaging.Message<ShowTimeResponse>("sample.console.showtime.response");
});

var sp = container.BuildServiceProvider();

_ = sp.GetRequiredService<IBootstrapper>().BootstrapAsync(cts.Token);

_ = Task.Run(
    async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(2000, cts.Token);

            await sp.GetRequiredService<IBus>()
                .PublishAsync(
                    new ShowTimeEvent(DateTime.UtcNow),
                    new PublishOptions
                    {
                        MessageName = "sample.console.showtime",
                        CallbackName = "sample.console.showtime.response",
                        DeliveryMode = DeliveryMode.Durable,
                    },
                    cts.Token
                );
        }
    },
    cts.Token
);

AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
Console.ReadLine();
