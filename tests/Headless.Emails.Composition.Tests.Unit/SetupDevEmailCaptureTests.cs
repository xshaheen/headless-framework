// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupDevEmailCaptureTests : TestBase
{
    [Fact]
    public async Task should_share_one_capture_store_between_the_default_and_named_senders()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
        {
            setup.UseDevelopment();
            setup.AddNamed("audit", static instance => instance.UseDevelopment());
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.GetRequiredService<IEmailSender>().SendAsync(_Request("default"), AbortToken);
        await provider.GetRequiredKeyedService<IEmailSender>("audit").SendAsync(_Request("named"), AbortToken);

        // then
        var store = provider.GetRequiredService<IEmailCaptureStore>();
        store.Should().BeSameAs(provider.GetRequiredService<IEmailCaptureStore>());
        store.Query().Select(x => (x.SenderName, x.Subject)).Should().Equal(("audit", "named"), ("", "default"));
    }

    [Fact]
    public void should_size_the_shared_store_from_the_largest_capacity()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
        {
            setup.UseDevelopment(static options => options.CaptureCapacity = 20);
            setup.AddNamed(
                "audit",
                static instance => instance.UseDevelopment((options, _) => options.CaptureCapacity = 50)
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailCaptureStore>().Capacity.Should().Be(50);
    }

    [Fact]
    public void should_default_to_500_emails()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.UseDevelopment());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEmailCaptureStore>().Capacity.Should().Be(500);
    }

    [Fact]
    public void should_bind_options_from_configuration()
    {
        // given
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["CaptureCapacity"] = "7",
                    ["MaxCapturedAttachmentBytes"] = "1024",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
        {
            setup.UseDevelopment(config);
            setup.AddNamed("audit", instance => instance.UseDevelopment(config));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var monitor = provider.GetRequiredService<IOptionsMonitor<DevEmailOptions>>();
        monitor.Get("audit").MaxCapturedAttachmentBytes.Should().Be(1024);
        provider.GetRequiredService<IEmailCaptureStore>().Capacity.Should().Be(7);
    }

    [Fact]
    public void should_register_named_sender_with_a_file_path()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.AddNamed("sink", static i => i.UseDevelopment("out.txt")));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<DevEmailOptions>>().Get("sink").FilePath.Should().Be("out.txt");
        provider.GetRequiredService<IEmailCaptureStore>().Should().NotBeNull();
    }

    [Fact]
    public void should_reject_a_zero_capacity()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.UseDevelopment(static options => options.CaptureCapacity = 0));
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IEmailCaptureStore>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_register_no_capture_store_for_the_noop_sender()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        provider.GetService<IEmailCaptureStore>().Should().BeNull();
    }

    private static SendSingleEmailRequest _Request(string subject)
    {
        return new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = subject,
            MessageText = "body",
        };
    }
}
