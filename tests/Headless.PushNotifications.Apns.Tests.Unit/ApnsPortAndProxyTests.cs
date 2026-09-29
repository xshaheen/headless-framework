// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Alternative-port and proxy options: <see cref="ApnsOptions.UseAlternativePort"/> moves both environments to
/// port 2197, and <see cref="ApnsOptions.Proxy"/> lands on the primary handler so every pooled connection uses it.
/// </summary>
public sealed class ApnsPortAndProxyTests : TestBase
{
    [Theory]
    [InlineData(ApnsEnvironment.Production, false, "https://api.push.apple.com/")]
    [InlineData(ApnsEnvironment.Production, true, "https://api.push.apple.com:2197/")]
    [InlineData(ApnsEnvironment.Sandbox, false, "https://api.sandbox.push.apple.com/")]
    [InlineData(ApnsEnvironment.Sandbox, true, "https://api.sandbox.push.apple.com:2197/")]
    public void should_select_the_environment_host_and_port_from_the_options(
        ApnsEnvironment environment,
        bool useAlternativePort,
        string expected
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(o =>
            {
                _ConfigureKey(o);
                o.Environment = environment;
                o.UseAlternativePort = useAlternativePort;
            });
            setup.AddNamed(
                "named",
                i =>
                    i.UseApns(o =>
                    {
                        _ConfigureKey(o);
                        o.Environment = environment;
                        o.UseAlternativePort = useAlternativePort;
                    })
            );
        });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // when
        using var defaultClient = factory.CreateClient("Headless:Apns");
        using var namedClient = factory.CreateClient("Headless:Apns:named");

        // then
        defaultClient.BaseAddress.Should().Be(new Uri(expected));
        namedClient.BaseAddress.Should().Be(new Uri(expected));
    }

    [Fact]
    public async Task should_apply_the_configured_proxy_to_the_primary_handler()
    {
        // given
        var proxy = new CountingProxy();
        await using var server = await FakeApnsServer.StartAsync(AbortToken);
        SocketsHttpHandler? seen = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
            setup.RegisterDefaultProvider(s =>
                SetupApnsPushNotifications.AddApnsCore(
                    s,
                    name: null,
                    (collection, name) =>
                        collection.Configure<ApnsOptions, ApnsOptionsValidator>(
                            options =>
                            {
                                server.ConfigureOptions(options);
                                options.Proxy = proxy;
                            },
                            name
                        ),
                    client => client.BaseAddress = server.BaseAddress,
                    resilience => resilience.Retry.Delay = TimeSpan.Zero,
                    handler => seen = handler
                )
            )
        );
        using var container = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        // when
        using var client = container.GetRequiredService<IHttpClientFactory>().CreateClient("Headless:Apns");
        var response = await container
            .GetRequiredService<IPushNotificationService>()
            .SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);

        // then
        seen.Should().NotBeNull();
        seen!.Proxy.Should().BeSameAs(proxy);
        // The handler consulted the proxy for the request's destination: this proxy bypasses loopback, so the send
        // still succeeds while proving the proxy sits in the decision path.
        response.IsSucceeded().Should().BeTrue();
        proxy.Consulted.Should().BeTrue();
    }

    /// <summary>
    /// An <see cref="IWebProxy"/> that records whether the handler asked it about a destination. Loopback is
    /// bypassed so the in-process test double is reached directly, exactly as a corporate proxy bypasses internal
    /// hosts.
    /// </summary>
    private sealed class CountingProxy : IWebProxy
    {
        public bool Consulted { get; private set; }

        public ICredentials? Credentials
        {
            get => null;
            set { }
        }

        public Uri? GetProxy(Uri destination)
        {
            Consulted = true;

            return destination;
        }

        public bool IsBypassed(Uri host)
        {
            Consulted = true;

            return IPAddress.IsLoopback(IPAddress.Parse(host.Host));
        }
    }

    [Fact]
    public async Task should_default_to_no_proxy()
    {
        // given
        await using var server = await FakeApnsServer.StartAsync(AbortToken);
        SocketsHttpHandler? seen = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
            setup.RegisterDefaultProvider(s =>
                SetupApnsPushNotifications.AddApnsCore(
                    s,
                    name: null,
                    (collection, name) =>
                        collection.Configure<ApnsOptions, ApnsOptionsValidator>(
                            options =>
                            {
                                server.ConfigureOptions(options);
                            },
                            name
                        ),
                    client => client.BaseAddress = server.BaseAddress,
                    resilience => resilience.Retry.Delay = TimeSpan.Zero,
                    handler => seen = handler
                )
            )
        );
        using var container = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        // when
        using var client = container.GetRequiredService<IHttpClientFactory>().CreateClient("Headless:Apns");

        // then
        seen.Should().NotBeNull();
        seen!.Proxy.Should().BeNull();
    }

    [Fact]
    public void should_copy_the_port_and_proxy_options_from_a_prebuilt_instance()
    {
        // given
        var proxy = new WebProxy(new Uri("http://proxy.internal:3128"));
        var options = new ApnsOptions
        {
            KeyId = FakeApnsServer.KeyId,
            TeamId = FakeApnsServer.TeamId,
            PrivateKey = _P256KeyPem,
            BundleId = "com.example.prebuilt",
            UseAlternativePort = true,
            Proxy = proxy,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(options);
            setup.AddNamed("named", i => i.UseApns(options));
        });
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ApnsOptions>>();

        // when
        var resolved = new[] { monitor.Get(Options.DefaultName), monitor.Get("named") };

        // then
        resolved
            .Should()
            .AllSatisfy(r =>
            {
                r.UseAlternativePort.Should().BeTrue();
                r.Proxy.Should().BeSameAs(proxy);
            });
    }

    private static void _ConfigureKey(ApnsOptions options)
    {
        // The endpoint is decided at client build time, before any key is used, but the validator still requires a
        // genuine P-256 key, so this class shares one.
        options.KeyId = FakeApnsServer.KeyId;
        options.TeamId = FakeApnsServer.TeamId;
        options.PrivateKey = _P256KeyPem;
        options.BundleId = "com.example.app";
    }

    private static readonly string _P256KeyPem = _ExportP256Key();

    private static string _ExportP256Key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }
}
