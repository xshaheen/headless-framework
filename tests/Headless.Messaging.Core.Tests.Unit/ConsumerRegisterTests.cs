using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

    [Fact]
    public async Task resume_group_async_propagates_resume_failures_after_logging_them()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        var expected = new InvalidOperationException("resume failed");

        client.ResumeAsync(Arg.Any<CancellationToken>()).Returns<ValueTask>(_ => ValueTask.FromException(expected));

        var handleType = typeof(ConsumerRegister).GetNestedType(
            "GroupHandle",
            System.Reflection.BindingFlags.NonPublic
        )!;
        var handle = Activator.CreateInstance(handleType, nonPublic: true)!;

        handleType.GetProperty("Logger")!.SetValue(handle, NullLogger<ConsumerRegister>.Instance);
        handleType.GetProperty("Cts")!.SetValue(handle, new CancellationTokenSource());
        handleType.GetProperty("GroupName")!.SetValue(handle, "payments");
        handleType.GetProperty("ConsumerTasks")!.SetValue(handle, new List<Task>());

        var addClient = handleType.GetMethod("AddClientAsync")!;
        await ((ValueTask)addClient.Invoke(handle, [client])!);

        var resumeGroup = typeof(ConsumerRegister).GetMethod(
            "_ResumeGroupAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        )!;

        var act = async () => await ((ValueTask)resumeGroup.Invoke(register, [handle])!);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("resume failed");
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
