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
        setup.Subscribe<EventConsumer>().Topic("sample.console.showtime");
        // Console app does not support dashboard
        setup.UseInMemoryStorage();
        setup.UseInMemoryMessageQueue();
    })
    .AddSubscribeFilter<CustomConsumerFilter>();

var sp = container.BuildServiceProvider();

_ = sp.GetRequiredService<IBootstrapper>().BootstrapAsync(cts.Token);

_ = Task.Run(
    async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(2000, cts.Token);

            await sp.GetRequiredService<IOutboxPublisher>()
                .PublishAsync(DateTime.UtcNow, new PublishOptions { Topic = "sample.console.showtime" }, cts.Token);
        }
    },
    cts.Token
);

AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
Console.ReadLine();
