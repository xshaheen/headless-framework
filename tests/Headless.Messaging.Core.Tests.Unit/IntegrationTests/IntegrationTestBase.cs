using System.Collections.ObjectModel;
using Framework.Testing.Tests;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.IntegrationTests;

public abstract class IntegrationTestBase : TestBase
{
    private readonly ITestOutputHelper _testOutput;
    protected CancellationTokenSource CancellationTokenSource { get; } = new(TimeSpan.FromSeconds(10));
    protected ServiceProvider Container { get; private set; } = null!;
    protected ObservableCollection<object> HandledMessages { get; } = [];
    protected IOutboxPublisher Publisher { get; private set; } = null!;

    protected IntegrationTestBase(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    protected IServiceScope Scope { get; private set; } = null!;

    protected CancellationToken CancellationToken => CancellationTokenSource.Token;

    public override ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddTestSetup(_testOutput);
        services.AddSingleton(new MessageQueueMarkerService("Broker"));
        services.AddSingleton(new MessageStorageMarkerService("Storage"));
        services.AddSingleton(_ => new TestMessageCollector(HandledMessages));

        ConfigureServices(services);

        Container = services.BuildTestContainer(CancellationToken);
        Scope = Container.CreateScope();
        Publisher = Scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        return base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        Scope.Dispose();
        await Container.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    protected abstract void ConfigureServices(IServiceCollection services);
}
