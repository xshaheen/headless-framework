using System.Collections.ObjectModel;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.IntegrationTests;

public abstract class IntegrationTestBase : TestBase
{
    protected CancellationTokenSource CancellationTokenSource { get; } = new(TimeSpan.FromSeconds(10));
    protected ServiceProvider Container { get; }
    protected ObservableCollection<object> HandledMessages { get; } = [];
    protected IOutboxPublisher Publisher { get; }

    protected IntegrationTestBase(ITestOutputHelper testOutput)
    {
        var services = new ServiceCollection();
        services.AddTestSetup(testOutput);
        services.AddSingleton(new CapMessageQueueMakerService("Broker"));
        services.AddSingleton(new CapStorageMarkerService("Storage"));
        services.AddSingleton(_ => new TestMessageCollector(HandledMessages));

        ConfigureServices(services);

        Container = services.BuildTestContainer(CancellationToken);
        Scope = Container.CreateScope();
        Publisher = Scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();
    }

    protected IServiceScope Scope { get; }

    protected CancellationToken CancellationToken => CancellationTokenSource.Token;

    protected override async ValueTask DisposeAsyncCore()
    {
        Scope.Dispose();
        await Container.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    protected abstract void ConfigureServices(IServiceCollection services);
}
