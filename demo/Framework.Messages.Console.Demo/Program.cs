using Demo;
using Framework.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using var cts = new CancellationTokenSource();
var container = new ServiceCollection();

container.AddLogging(x => x.AddConsole());

container.AddMessages(messaging =>
{
    messaging.Consumer<EventConsumer>().Topic("sample.console.showtime").Build();
});

container
    .AddCap(x =>
    {
        //console app does not support dashboard

        x.UseInMemoryStorage();
        x.UseInMemoryMessageQueue();
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
                .PublishAsync("sample.console.showtime", DateTime.Now, cancellationToken: cts.Token);
        }
    },
    cts.Token
);

AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
Console.ReadLine();
