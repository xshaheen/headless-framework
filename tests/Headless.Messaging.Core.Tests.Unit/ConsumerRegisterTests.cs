using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ConsumerRegisterTests : TestBase
{
    [Fact]
    public async Task restart_keeps_consumer_shutdown_linked_to_the_host_token()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        using var hostCts = new CancellationTokenSource();

        await register.StartAsync(hostCts.Token);
        await register.ReStartAsync(force: true);

        await hostCts.CancelAsync();

        var field = typeof(ConsumerRegister).GetField(
            "_stoppingCts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        var linkedCts = (CancellationTokenSource)field!.GetValue(register)!;

        linkedCts.IsCancellationRequested.Should().BeTrue();

        await register.DisposeAsync();
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.UseConventions(c =>
            {
                c.UseApplicationId("messaging-tests");
                c.UseVersion("v1");
            });
        });

        return services.BuildServiceProvider();
    }
}
