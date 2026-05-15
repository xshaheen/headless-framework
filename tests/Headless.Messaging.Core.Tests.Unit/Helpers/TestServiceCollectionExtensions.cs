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
            services.AddHeadlessMessaging(setup =>
            {
                setup.SubscribeFromAssembly(typeof(TestServiceCollectionExtensions).Assembly);
                setup.Options.DefaultGroupName = TestGroupName;
                setup.UseInMemoryMessageQueue();
                setup.UseInMemoryStorage();
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
