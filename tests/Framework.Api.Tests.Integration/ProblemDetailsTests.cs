// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ProblemDetailsTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task should_return_hello_world()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Hello World!");
    }

    private async Task<WebApplicationFactory<Program>> _CreateDefaultFactory(
        Action<WebHostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IWebHostBuilder>? configureHost = null
    )
    {
        await using var factory = new WebApplicationFactory<Program>();

        factory.ClientOptions.AllowAutoRedirect = false;

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(EnvironmentNames.Test);
            builder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders().AddProvider(LoggerProvider));
            configureHost?.Invoke(builder);

            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }
}
