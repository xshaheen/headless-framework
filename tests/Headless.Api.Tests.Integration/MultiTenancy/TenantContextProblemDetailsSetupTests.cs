// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed class TenantContextProblemDetailsSetupTests : TestBase
{
    [Fact]
    public async Task should_register_handler_via_simple_action_overload()
    {
        // given
        await using var app = _CreateApp(builder =>
            builder.Services.AddTenantContextProblemDetails(o => o.TypeUriPrefix = "https://errors.example.com/tenancy")
        );

        // when
        var handlers = app.Services.GetServices<IExceptionHandler>().ToList();
        var options = app.Services.GetRequiredService<IOptions<TenantContextProblemDetailsOptions>>().Value;

        // then
        handlers.Should().ContainSingle(h => h is TenantContextExceptionHandler);
        options.TypeUriPrefix.Should().Be("https://errors.example.com/tenancy");
    }

    [Fact]
    public async Task should_register_handler_via_configuration_overload()
    {
        // given
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["TypeUriPrefix"] = "https://configured.example.com/tenancy",
                    ["ErrorCode"] = "configured.code",
                }
            )
            .Build();

        await using var app = _CreateApp(builder => builder.Services.AddTenantContextProblemDetails(config));

        // when
        var handlers = app.Services.GetServices<IExceptionHandler>().ToList();
        var options = app.Services.GetRequiredService<IOptions<TenantContextProblemDetailsOptions>>().Value;

        // then
        handlers.Should().ContainSingle(h => h is TenantContextExceptionHandler);
        options.TypeUriPrefix.Should().Be("https://configured.example.com/tenancy");
        options.ErrorCode.Should().Be("configured.code");
    }

    [Fact]
    public async Task should_register_handler_via_service_provider_action_overload()
    {
        // given - the (options, sp) overload typically resolves runtime DI services into options
        await using var app = _CreateApp(builder =>
            builder.Services.AddTenantContextProblemDetails(
                (options, sp) =>
                {
                    var marker = sp.GetService<_OptionsMarker>();
                    marker.Should().NotBeNull();
                    options.TypeUriPrefix = "https://service-provider.example.com/tenancy";
                }
            )
        );

        // when - resolving forces the configure callback to run
        var options = app.Services.GetRequiredService<IOptions<TenantContextProblemDetailsOptions>>().Value;
        var handlers = app.Services.GetServices<IExceptionHandler>().ToList();

        // then
        handlers.Should().ContainSingle(h => h is TenantContextExceptionHandler);
        options.TypeUriPrefix.Should().Be("https://service-provider.example.com/tenancy");
    }

    [Fact]
    public async Task should_be_idempotent_when_called_twice()
    {
        // given
        await using var app = _CreateApp(builder =>
        {
            builder.Services.AddTenantContextProblemDetails(o =>
                o.TypeUriPrefix = "https://errors.example.com/tenancy"
            );
            builder.Services.AddTenantContextProblemDetails(o =>
                o.TypeUriPrefix = "https://errors.example.com/tenancy"
            );
        });

        // when
        var handlers = app
            .Services.GetServices<IExceptionHandler>()
            .Where(h => h is TenantContextExceptionHandler)
            .ToList();

        // then - AddExceptionHandler<T> uses TryAddEnumerable internally; same descriptor only registers once
        handlers.Should().HaveCount(1);
    }

    [Fact]
    public async Task should_throw_options_validation_exception_when_type_uri_prefix_is_invalid()
    {
        // given
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddHeadlessApi();
        builder.Services.AddTenantContextProblemDetails(o => o.TypeUriPrefix = "not a url");

        var app = builder.Build();

        // when
        var act = async () => await app.StartAsync(AbortToken);

        // then - ValidateOnStart fires during host startup
        await act.Should().ThrowAsync<OptionsValidationException>();

        await app.DisposeAsync();
    }

    private WebApplication _CreateApp(Action<WebApplicationBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddHeadlessApi();
        builder.Services.AddSingleton<_OptionsMarker>();
        configure(builder);
        return builder.Build();
    }

    private sealed class _OptionsMarker;
}
