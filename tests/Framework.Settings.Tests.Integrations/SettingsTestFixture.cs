// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings.Tests.Integrations;

// Create a test fixture for the TestFixture and create a Generic host and add settings management services

public sealed class SettingsTestFixture
{
    public SettingsTestFixture()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(TimeProvider.System);

        builder
            .AddSettingsManagementCore()
            .AddSettingsManagementEntityFrameworkStorage(options =>
            {
                options.UseNpgsql("Host=localhost;Database=SettingsTest;Username=postgres;Password=postgres");
            });

        var host = builder.Build();

        ServiceProvider = host.Services;
    }

    public IServiceProvider ServiceProvider { get; }
}
