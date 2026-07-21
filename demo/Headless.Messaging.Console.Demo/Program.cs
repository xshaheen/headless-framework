// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using var cts = new CancellationTokenSource();
var container = new ServiceCollection();

container.AddLogging(x => x.AddConsole());

container
    .AddHeadlessMessaging(setup =>
    {
        setup.Bus.ForMessage<ShowTimeEvent>(message =>
            message.MessageName("sample.console.showtime").Consumer<EventConsumer>()
        );
        setup.Bus.ForMessage<ShowTimeResponse>(message =>
            message.MessageName("sample.console.showtime.response").Consumer<ShowTimeResponseConsumer>()
        );
        // Console app does not support dashboard
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

            await sp.GetRequiredService<IOutboxBus>()
                .PublishAsync(
                    new ShowTimeEvent(DateTime.UtcNow),
                    new PublishOptions
                    {
                        MessageName = "sample.console.showtime",
                        CallbackName = "sample.console.showtime.response",
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
