// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Mailkit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Tests;

public sealed class MailkitSmtpOptionsValidatorTests
{
    private readonly MailkitSmtpOptionsValidator _validator = new();

    [Fact]
    public void valid_options_should_pass()
    {
        _validator.Validate(_Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void empty_server_should_fail()
    {
        _validator.Validate(new MailkitSmtpOptions { Server = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void zero_pool_size_should_be_allowed()
    {
        _validator.Validate(_Valid(maxPoolSize: 0)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void negative_pool_size_should_fail()
    {
        _validator.Validate(_Valid(maxPoolSize: -1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void timeout_beyond_int_milliseconds_should_fail()
    {
        _validator.Validate(_Valid(timeout: TimeSpan.FromDays(30))).IsValid.Should().BeFalse();
    }

    private static MailkitSmtpOptions _Valid(int maxPoolSize = 10, TimeSpan? timeout = null) =>
        new()
        {
            Server = "smtp.example.com",
            Port = 587,
            MaxPoolSize = maxPoolSize,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
}

public sealed class SmtpClientPooledObjectPolicyTests
{
    [Fact]
    public void create_should_apply_the_configured_timeout()
    {
        var policy = _Policy(TimeSpan.FromSeconds(5));

        using var client = policy.Create();

        client.Timeout.Should().Be(5000);
    }

    [Fact]
    public void return_should_discard_a_disconnected_client()
    {
        var policy = _Policy(TimeSpan.FromSeconds(5));
        var client = new SmtpClient();

        policy.Return(client).Should().BeFalse();
    }

    private static SmtpClientPooledObjectPolicy _Policy(TimeSpan timeout)
    {
        var options = new MailkitSmtpOptions { Server = "smtp.example.com", Timeout = timeout };
        var monitor = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.CurrentValue.Returns(options);
        monitor.Get(Arg.Any<string>()).Returns(options);
        return new SmtpClientPooledObjectPolicy(monitor, optionsName: null);
    }
}

public sealed class MailkitEmailSenderTests
{
    [Fact]
    public async Task missing_body_should_throw_before_touching_the_pool()
    {
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        var options = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        var sender = new MailkitEmailSender(pool, options, optionsName: null, NullLogger<MailkitEmailSender>.Instance);

        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "no body",
        };

        var act = async () => await sender.SendAsync(request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        pool.DidNotReceive().Get();
    }
}
