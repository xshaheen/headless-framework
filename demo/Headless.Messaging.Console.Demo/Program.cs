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
        setup.Bus.ForMessage<ShowTimeEvent>(message =>
            message
                .MessageName("sample.console.showtime")
                .Consumer<EventConsumer>(consumer =>
                    consumer.ConsumerIdentity("console.showtime").ContractVersion("v1")
                )
        );
        setup.Bus.ForMessage<ShowTimeResponse>(message =>
            message
                .MessageName("sample.console.showtime.response")
                .Consumer<ShowTimeResponseConsumer>(consumer =>
                    consumer.ConsumerIdentity("console.showtime-response").ContractVersion("v1")
                )
        );
        // Console app does not support dashboard
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
        setup.UseInMemory();
    })
    .AddBusConsumeMiddleware<CustomConsumerMiddleware>();

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

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
#pragma warning restore MA0045
Console.ReadLine();
