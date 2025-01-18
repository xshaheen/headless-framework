using Microsoft.Extensions.Hosting;

namespace Tests.TestSetup;

[Collection(nameof(SettingsTestFixture))]
public abstract class SettingsTestBase(SettingsTestFixture fixture)
{
    protected HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.ConfigureSettingsServices(fixture.ConnectionString);

        return builder;
    }

    protected IHost CreateHost(Action<HostApplicationBuilder>? configure = null)
    {
        var builder = CreateHostBuilder();
        configure?.Invoke(builder);

        return builder.Build();
    }
}
