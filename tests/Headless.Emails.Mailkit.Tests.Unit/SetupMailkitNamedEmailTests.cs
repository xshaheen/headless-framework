// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Headless.Emails.Mailkit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Tests;

public sealed class SetupMailkitNamedEmailTests
{
    // Build each named instance from its own in-memory configuration so each gets isolated settings.
    private static IConfiguration _SmtpConfig(
        string server,
        int port,
        string timeout = "00:00:30",
        int maxPoolSize = 10
    )
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Server"] = server,
                    ["Port"] = port.ToString(CultureInfo.InvariantCulture),
                    ["Timeout"] = timeout,
                    ["MaxPoolSize"] = maxPoolSize.ToString(CultureInfo.InvariantCulture),
                }
            )
            .Build();
    }

    [Fact]
    public void should_isolate_options_and_pools_across_named_instances()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "smtp1",
                instance => instance.UseMailkit(_SmtpConfig("h1", 25, timeout: "00:00:10", maxPoolSize: 3))
            );
            setup.AddNamed(
                "smtp2",
                instance => instance.UseMailkit(_SmtpConfig("h2", 587, timeout: "00:00:20", maxPoolSize: 7))
            );
        });
        using var provider = services.BuildServiceProvider();

        // then - each instance binds its own named options.
        var monitor = provider.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.Get("smtp1").Server.Should().Be("h1");
        monitor.Get("smtp1").Port.Should().Be(25);
        monitor.Get("smtp1").MaxPoolSize.Should().Be(3);
        monitor.Get("smtp2").Server.Should().Be("h2");
        monitor.Get("smtp2").Port.Should().Be(587);
        monitor.Get("smtp2").MaxPoolSize.Should().Be(7);

        // each instance owns a distinct pool.
        var pool1 = provider.GetRequiredKeyedService<ObjectPool<SmtpClient>>("smtp1");
        var pool2 = provider.GetRequiredKeyedService<ObjectPool<SmtpClient>>("smtp2");
        pool1.Should().NotBeSameAs(pool2);

        // the keyed policy builds clients from its own named Timeout — proving it reads Get(name), not the
        // default-bound CurrentValue (the bug this seam exists to prevent).
        var client1 = pool1.Get();
        var client2 = pool2.Get();
        try
        {
            client1.Timeout.Should().Be(10_000);
            client2.Timeout.Should().Be(20_000);
        }
        finally
        {
            pool1.Return(client1);
            pool2.Return(client2);
        }
    }

    [Fact]
    public void should_resolve_named_mailkit_sender_from_delegate_overload()
    {
        // given - the Action<MailkitSmtpOptions> overload (settable options); previously uncompilable.
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "smtp",
                instance =>
                    instance.UseMailkit(o =>
                    {
                        o.Server = "smtp.example.com";
                        o.Port = 2525;
                    })
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IEmailSender>("smtp").Should().BeOfType<MailkitEmailSender>();
        var monitor = provider.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.Get("smtp").Server.Should().Be("smtp.example.com");
        monitor.Get("smtp").Port.Should().Be(2525);
    }

    [Fact]
    public void should_resolve_named_mailkit_sender_alongside_default_noop()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("smtp", instance => instance.UseMailkit(_SmtpConfig("relay", 2525)));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("smtp").Should().BeOfType<MailkitEmailSender>();
        provider.GetRequiredService<IEmailSenderProvider>().GetSender("smtp").Should().BeOfType<MailkitEmailSender>();
    }

    [Fact]
    public void should_keep_default_and_named_mailkit_settings_isolated()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when - a default Mailkit sender (host hd) plus a named Mailkit instance (host hn).
        services.AddHeadlessEmails(setup =>
        {
            setup.UseMailkit(_SmtpConfig("hd", 1025));
            setup.AddNamed("relay", instance => instance.UseMailkit(_SmtpConfig("hn", 2025)));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default (unkeyed) options and the named options do not bleed across the keyed boundary.
        var monitor = provider.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.CurrentValue.Server.Should().Be("hd");
        monitor.Get("relay").Server.Should().Be("hn");

        provider.GetRequiredService<IEmailSender>().Should().BeOfType<MailkitEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("relay").Should().BeOfType<MailkitEmailSender>();
    }
}
