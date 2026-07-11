// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Mailkit;
using Headless.Testing.Tests;
using MailKit.Security;
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
        using var client = new SmtpClient();

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

public sealed class MailkitEmailSenderTests : TestBase
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

        var act = async () => await sender.SendAsync(request, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        pool.DidNotReceive().Get();
    }

    [Fact]
    public async Task connect_transport_fault_should_return_failed()
    {
        // A connect-level fault (IOException) must be surfaced as a failed response, not thrown.
        var client = new FakeSmtpClient(_ => Task.FromException(new IOException("connection reset")));
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("connection reset");
    }

    [Fact]
    public async Task connect_timeout_cancellation_should_return_failed()
    {
        // A timeout-CTS-induced cancellation (the caller's token is NOT cancelled) is a delivery failure,
        // not a caller cancellation, so it is returned rather than thrown.
        var client = new FakeSmtpClient(_ => Task.FromException(new OperationCanceledException()));
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().NotBeNull();
    }

    [Fact]
    public async Task caller_cancellation_should_propagate()
    {
        // Only the caller's own cancellation propagates as OperationCanceledException.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new FakeSmtpClient(ct => Task.FromException(new OperationCanceledException(ct)));
        var sender = _Sender(client);

        var act = async () => await sender.SendAsync(_Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MailkitEmailSender _Sender(SmtpClient client)
    {
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        pool.Get().Returns(client);
        var options = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        options
            .Get(Arg.Any<string>())
            .Returns(new MailkitSmtpOptions { Server = "smtp.example.com", Timeout = TimeSpan.FromSeconds(5) });

        return new MailkitEmailSender(pool, options, optionsName: null, NullLogger<MailkitEmailSender>.Instance);
    }

    private static SendSingleEmailRequest _Request() =>
        new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };

    // A test double whose connect step is scripted; no network is touched. A fresh client reports
    // IsConnected == false, so the sender always reaches ConnectAsync.
    private sealed class FakeSmtpClient(Func<CancellationToken, Task> onConnect) : SmtpClient
    {
        public override Task ConnectAsync(
            string host,
            int port,
            SecureSocketOptions options,
            CancellationToken cancellationToken = default
        ) => onConnect(cancellationToken);
    }
}
