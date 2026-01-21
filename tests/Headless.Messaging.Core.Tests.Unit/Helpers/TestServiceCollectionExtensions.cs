using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Helpers;

public static class TestServiceCollectionExtensions
{
    public const string TestGroupName = "Test";

    extension(IServiceCollection services)
    {
        public void AddTestSetup(ITestOutputHelper testOutput)
        {
            services.AddLogging(x => x.AddTestLogging(testOutput));
            services.AddMessages(x =>
            {
                x.ScanConsumers(typeof(TestServiceCollectionExtensions).Assembly);
                x.DefaultGroupName = TestGroupName;
                x.UseInMemoryMessageQueue();
                x.UseInMemoryStorage();
            });
        }

        public ServiceProvider BuildTestContainer(CancellationToken cancellationToken)
        {
            var container = services.BuildServiceProvider();
            container.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken).Wait(cancellationToken);
            return container;
        }
    }
}
