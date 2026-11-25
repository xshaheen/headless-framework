// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Mailkit;
using Headless.Testing.Tests;
using MailKit;
using MailKit.Net.Smtp;
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
    public void should_pass_when_valid_options()
    {
        _validator.Validate(_Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_empty_server()
    {
        _validator.Validate(new MailkitSmtpOptions { Server = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_be_allowed_when_zero_pool_size()
    {
        _validator.Validate(_Valid(maxPoolSize: 0)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_negative_pool_size()
    {
        _validator.Validate(_Valid(maxPoolSize: -1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_timeout_beyond_int_milliseconds()
    {
        _validator.Validate(_Valid(timeout: TimeSpan.FromDays(30))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_negative_idle_probe_threshold()
    {
        _validator.Validate(_Valid(idleProbeThreshold: TimeSpan.FromSeconds(-1))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_be_allowed_when_zero_idle_probe_threshold()
    {
        _validator.Validate(_Valid(idleProbeThreshold: TimeSpan.Zero)).IsValid.Should().BeTrue();
    }

    private static MailkitSmtpOptions _Valid(
        int maxPoolSize = 10,
        TimeSpan? timeout = null,
        TimeSpan? idleProbeThreshold = null
    )
    {
        return new()
        {
            Server = "smtp.example.com",
            Port = 587,
            MaxPoolSize = maxPoolSize,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            IdleProbeThreshold = idleProbeThreshold ?? TimeSpan.FromSeconds(30),
        };
    }
}

public sealed class SmtpClientPooledObjectPolicyTests
{
    [Fact]
    public void should_create_a_client_that_tracks_its_last_send_when_create()
    {
        var policy = _Policy(TimeSpan.FromSeconds(5));

        using var client = policy.Create();

        client.Should().BeOfType<PooledSmtpClient>();
    }

    [Fact]
    public void should_apply_the_configured_timeout_when_create()
    {
        var policy = _Policy(TimeSpan.FromSeconds(5));

        using var client = policy.Create();

        client.Timeout.Should().Be(5000);
    }

    [Fact]
    public void should_discard_a_disconnected_client_when_return()
    {
        var policy = _Policy(TimeSpan.FromSeconds(5));
        using var client = new SmtpClient();

        policy.Return(client).Should().BeFalse();
    }

    [Fact]
    public void should_discard_an_unauthenticated_client_when_return_on_a_named_pool_with_credentials()
    {
        // Regression: Return() must read the named snapshot. Reading CurrentValue binds the DEFAULT options, so a
        // named pool whose credentials live only in its own section would skip the authentication check entirely
        // and pool a connected-but-unauthenticated client.
        var monitor = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.CurrentValue.Returns(new MailkitSmtpOptions { Server = "smtp.example.com" });
        monitor
            .Get("named")
            .Returns(
                new MailkitSmtpOptions
                {
                    Server = "smtp.example.com",
                    User = "user",
                    Password = "password",
                }
            );

        var policy = new SmtpClientPooledObjectPolicy(monitor, optionsName: "named");
        using var client = new FakeConnectedSmtpClient(isAuthenticated: false);

        policy.Return(client).Should().BeFalse();
    }

    [Fact]
    public void should_keep_an_authenticated_client_when_return_on_a_named_pool_with_credentials()
    {
        var monitor = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.CurrentValue.Returns(new MailkitSmtpOptions { Server = "smtp.example.com" });
        monitor
            .Get("named")
            .Returns(
                new MailkitSmtpOptions
                {
                    Server = "smtp.example.com",
                    User = "user",
                    Password = "password",
                }
            );

        var policy = new SmtpClientPooledObjectPolicy(monitor, optionsName: "named");
        using var client = new FakeConnectedSmtpClient(isAuthenticated: true);

        policy.Return(client).Should().BeTrue();
    }

    private static SmtpClientPooledObjectPolicy _Policy(TimeSpan timeout)
    {
        var options = new MailkitSmtpOptions { Server = "smtp.example.com", Timeout = timeout };
        var monitor = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        monitor.CurrentValue.Returns(options);
        monitor.Get(Arg.Any<string>()).Returns(options);
        return new SmtpClientPooledObjectPolicy(monitor, optionsName: null);
    }

    private sealed class FakeConnectedSmtpClient(bool isAuthenticated) : SmtpClient
    {
        public override bool IsConnected => true;

        public override bool IsAuthenticated => isAuthenticated;
    }
}

public sealed class MailkitEmailSenderTests : TestBase
{
    [Fact]
    public async Task should_throw_before_touching_the_pool_when_missing_body()
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
    public async Task should_return_failed_when_connect_transport_fault()
    {
        // A connect-level fault (IOException) must be surfaced as a failed response, not thrown.
        using var client = new FakeSmtpClient(_ => Task.FromException(new IOException("connection reset")));
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("connection reset");
    }

    [Fact]
    public async Task should_return_failed_when_connect_timeout_cancellation()
    {
        // A timeout-CTS-induced cancellation (the caller's token is NOT cancelled) is a delivery failure,
        // not a caller cancellation, so it is returned rather than thrown.
        using var client = new FakeSmtpClient(_ => Task.FromException(new OperationCanceledException()));
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().NotBeNull();
    }

    [Fact]
    public async Task should_propagate_when_caller_cancellation()
    {
        // Only the caller's own cancellation propagates as OperationCanceledException.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var client = new FakeSmtpClient(ct => Task.FromException(new OperationCanceledException(ct)));
        var sender = _Sender(client);

        var act = async () => await sender.SendAsync(_Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_reconnect_and_send_when_pooled_connection_was_dropped_by_the_server()
    {
        // A pooled client still reports IsConnected after the server closed the idle session; only a round
        // trip reveals it. The send must reconnect instead of failing on the dead socket.
        using var client = new StaleConnectionSmtpClient();
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("250 2.0.0 queued");
        client.ConnectCount.Should().Be(1);
    }

    [Fact]
    public async Task should_discard_the_client_instead_of_pooling_it_when_send_fails()
    {
        using var client = new FakeSmtpClient(_ => Task.FromException(new IOException("connection reset")));
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        pool.Get().Returns(client);
        var sender = _Sender(pool);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        pool.DidNotReceive().Return(Arg.Any<SmtpClient>());
    }

    [Fact]
    public async Task should_return_the_client_to_the_pool_when_send_succeeds()
    {
        using var client = new StaleConnectionSmtpClient();
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        pool.Get().Returns(client);
        var sender = _Sender(pool);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeTrue();
        pool.Received(1).Return(client);
    }

    [Fact]
    public async Task should_skip_the_noop_probe_when_the_pooled_connection_was_used_within_the_threshold()
    {
        // given: a pooled connection that just completed a send
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();

        // when
        var result = await sender.SendAsync(_Request(), AbortToken);

        // then
        result.Success.Should().BeTrue();
        client.NoOpCount.Should().Be(0);
        client.ConnectCount.Should().Be(1);
        client.SendCount.Should().Be(2);
    }

    [Fact]
    public async Task should_probe_before_every_send_when_the_idle_probe_threshold_is_zero()
    {
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.Zero);
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();

        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();

        client.NoOpCount.Should().Be(2);
    }

    [Fact]
    public async Task should_probe_when_the_pooled_connection_sat_idle_past_the_threshold()
    {
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMilliseconds(1));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();

        // Waiting longer than the threshold can only make the connection more idle, so the wait cannot flake.
        // real-wait: PooledSmtpClient.WasUsedWithin reads Stopwatch (real idle time, by design), which no test clock can move
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();

        client.NoOpCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(DroppedSessionFaults))]
    public async Task should_reconnect_and_retry_once_when_an_unprobed_connection_fails_before_the_sender_is_accepted(
        string fault
    )
    {
        // given: a recently used connection the server closed without the client noticing
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ => throw _DroppedSessionFault(fault));

        // when
        var result = await sender.SendAsync(_Request(), AbortToken);

        // then
        result.Success.Should().BeTrue();
        client.SendCount.Should().Be(3);
        client.ConnectCount.Should().Be(2);
        client.DisconnectCount.Should().Be(1);
        client.Requests[2].Should().NotBeSameAs(client.Requests[1], "each attempt tracks its own MAIL FROM outcome");
    }

    public static TheoryData<string> DroppedSessionFaults => ["io", "timeout", "protocol", "not-connected", "421"];

    [Fact]
    public async Task should_not_retry_when_the_connection_drops_after_the_server_accepted_the_sender()
    {
        // The server may already hold the message once MAIL FROM is accepted, so a retry could deliver it twice.
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(c =>
        {
            c.AcceptSender();
            throw new IOException("connection reset during DATA");
        });

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        client.SendCount.Should().Be(2);
        client.ConnectCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_retry_when_the_server_rejects_the_sender()
    {
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ =>
            throw new SmtpCommandException(
                SmtpErrorCode.SenderNotAccepted,
                SmtpStatusCode.MailboxUnavailable,
                "sender rejected"
            )
        );

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        client.SendCount.Should().Be(2);
    }

    [Fact]
    public async Task should_retry_only_once_when_the_retried_send_also_fails()
    {
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ => throw new IOException("connection reset"));
        client.Behaviors.Enqueue(_ => throw new IOException("connection reset again"));

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("connection reset again");
        client.SendCount.Should().Be(3);
    }

    [Fact]
    public async Task should_not_retry_when_a_probed_connection_fails_on_send()
    {
        // A connection that just answered NOOP failing on send is not a stale idle session; surface it.
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.Zero);
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ => throw new IOException("connection reset"));

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        client.SendCount.Should().Be(2);
        client.ConnectCount.Should().Be(1);
    }

    [Fact]
    public async Task should_propagate_without_retry_when_the_caller_cancels_during_send()
    {
        using var cts = new CancellationTokenSource();
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var act = async () => await sender.SendAsync(_Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.SendCount.Should().Be(2);
    }

    [Fact]
    public async Task should_report_the_full_multi_line_server_response_when_the_send_succeeds()
    {
        using var client = new ScriptedSmtpClient();
        client.Behaviors.Enqueue(c =>
        {
            c.AcceptSender();
            return "2.0.0 Ok: queued as 4F2A1\r\n2.0.0 delivered to relay";
        });
        var sender = _Sender(client);

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("2.0.0 Ok: queued as 4F2A1\r\n2.0.0 delivered to relay");
    }

    [Fact]
    public async Task should_return_failed_and_discard_the_client_without_retry_when_the_server_requires_authentication()
    {
        // MailKit 5 reports a 530 reply as SmtpServiceNotAuthenticatedException. It is not a dropped session,
        // so even an unprobed connection is not retried, and the client is not pooled.
        using var client = new ScriptedSmtpClient();
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        pool.Get().Returns(client);
        var sender = _Sender(pool, idleProbeThreshold: TimeSpan.FromMinutes(5));
        (await sender.SendAsync(_Request(), AbortToken)).Success.Should().BeTrue();
        client.Behaviors.Enqueue(_ =>
            throw new SmtpServiceNotAuthenticatedException(
                SmtpCommand.MailFrom,
                new SmtpResponse(SmtpStatusCode.AuthenticationRequired, "5.7.0 Authentication required")
            )
        );

        var result = await sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("5.7.0 Authentication required");
        client.SendCount.Should().Be(2);
        client.ConnectCount.Should().Be(1);
        pool.Received(1).Return(client);
    }

    [Fact]
    public async Task should_send_an_smtp_request_whose_envelope_comes_from_the_message_headers()
    {
        // MailKit builds the envelope from To, Cc, and Bcc when the request names no recipients, so a Bcc
        // recipient is delivered while its header is hidden from the written message.
        using var client = new ScriptedSmtpClient();
        var sender = _Sender(client);
        var request = _Request() with
        {
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("to@example.com")],
                BccAddresses = [new EmailRequestAddress("bcc@example.com")],
            },
        };

        (await sender.SendAsync(request, AbortToken)).Success.Should().BeTrue();

        var sent = client.Requests.Should().ContainSingle().Which.Should().BeAssignableTo<SmtpSendRequest>().Subject;
        sent.Sender.Should().BeNull();
        sent.Recipients.Should().BeNull();
        sent.Message.Bcc.Mailboxes.Select(m => m.Address).Should().Equal("bcc@example.com");
    }

    private static Exception _DroppedSessionFault(string fault)
    {
        return fault switch
        {
            "io" => new IOException("connection reset"),
            "timeout" => new TimeoutException("Operation timed out after 5000 milliseconds"),
            "protocol" => new SmtpProtocolException("The SMTP server has unexpectedly disconnected."),
            "not-connected" => new ServiceNotConnectedException("not connected"),
            _ => new SmtpCommandException(
                SmtpErrorCode.SenderNotAccepted,
                SmtpStatusCode.ServiceNotAvailable,
                "4.4.2 idle timeout"
            ),
        };
    }

    private static MailkitEmailSender _Sender(SmtpClient client, TimeSpan? idleProbeThreshold = null)
    {
        var pool = Substitute.For<ObjectPool<SmtpClient>>();
        pool.Get().Returns(client);

        return _Sender(pool, idleProbeThreshold);
    }

    private static MailkitEmailSender _Sender(ObjectPool<SmtpClient> pool, TimeSpan? idleProbeThreshold = null)
    {
        var options = Substitute.For<IOptionsMonitor<MailkitSmtpOptions>>();
        options
            .Get(Arg.Any<string>())
            .Returns(
                new MailkitSmtpOptions
                {
                    Server = "smtp.example.com",
                    Timeout = TimeSpan.FromSeconds(5),
                    IdleProbeThreshold = idleProbeThreshold ?? TimeSpan.FromSeconds(30),
                }
            );

        return new MailkitEmailSender(pool, options, optionsName: null, NullLogger<MailkitEmailSender>.Instance);
    }

    private static SendSingleEmailRequest _Request()
    {
        return new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };
    }

    // A pooled client whose sends follow a script: each queued behavior handles one send, and a send with no
    // behavior queued succeeds after the server accepts the sender, as a live session does. The sender's
    // acceptance is reported to the request the client was handed, as MailKit reports it.
    private sealed class ScriptedSmtpClient : PooledSmtpClient
    {
        private bool _connected;
        private ISendRequest? _request;

        public Queue<Func<ScriptedSmtpClient, string>> Behaviors { get; } = new();

        public List<ISendRequest> Requests { get; } = [];

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public int NoOpCount { get; private set; }

        public int SendCount => Requests.Count;

        public override bool IsConnected => _connected;

        public override Task ConnectAsync(
            string host,
            int port = 0,
            SecureSocketOptions options = SecureSocketOptions.Auto,
            CancellationToken cancellationToken = default
        )
        {
            ConnectCount++;
            _connected = true;
            return Task.CompletedTask;
        }

        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            DisconnectCount++;
            _connected = false;
            return Task.CompletedTask;
        }

        public override Task NoOpAsync(CancellationToken cancellationToken = default)
        {
            NoOpCount++;
            return Task.CompletedTask;
        }

        public override Task<SendResult> SendAsync(ISendRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            _request = request;

            try
            {
                if (Behaviors.TryDequeue(out var behavior))
                {
                    return Task.FromResult(_Result(behavior(this)));
                }

                AcceptSender();
                return Task.FromResult(_Result("250 2.0.0 queued"));
            }
            catch (Exception e)
            {
                return Task.FromException<SendResult>(e);
            }
        }

        public void AcceptSender()
        {
            ((ISmtpSendRequest)_request!).OnSenderAccepted(
                new MimeKit.MailboxAddress("", "from@example.com"),
                new SmtpResponse(SmtpStatusCode.Ok, "2.1.0 sender ok")
            );
        }
    }

    // A pooled client whose server closed the session while it sat idle: it still reports IsConnected, the
    // next command fails the way MailKit fails on a closed socket, and only a fresh connect can send.
    private sealed class StaleConnectionSmtpClient : SmtpClient
    {
        private bool _connected = true;
        private bool _socketAlive;

        public int ConnectCount { get; private set; }

        public override bool IsConnected => _connected;

        public override Task ConnectAsync(
            string host,
            int port = 0,
            SecureSocketOptions options = SecureSocketOptions.Auto,
            CancellationToken cancellationToken = default
        )
        {
            ConnectCount++;
            _connected = true;
            _socketAlive = true;
            return Task.CompletedTask;
        }

        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            _connected = false;
            _socketAlive = false;
            return Task.CompletedTask;
        }

        public override Task NoOpAsync(CancellationToken cancellationToken = default)
        {
            return _socketAlive ? Task.CompletedTask : _Dropped<object?>();
        }

        public override Task<SendResult> SendAsync(ISendRequest request, CancellationToken cancellationToken = default)
        {
            return _socketAlive ? Task.FromResult(_Result("250 2.0.0 queued")) : _Dropped<SendResult>();
        }

        private Task<T> _Dropped<T>()
        {
            _connected = false;
            return Task.FromException<T>(new SmtpProtocolException("The SMTP server has unexpectedly disconnected."));
        }
    }

    private static SendResult _Result(string responseText)
    {
        return new SmtpSendResult(SmtpStatusCode.Ok, responseText, [], []);
    }

    // A test double whose connect step is scripted; no network is touched. A fresh client reports
    // IsConnected == false, so the sender always reaches ConnectAsync.
    private sealed class FakeSmtpClient(Func<CancellationToken, Task> onConnect) : SmtpClient
    {
        public override Task ConnectAsync(
            string host,
            int port = 0,
            SecureSocketOptions options = SecureSocketOptions.Auto,
            CancellationToken cancellationToken = default
        )
        {
            return onConnect(cancellationToken);
        }
    }
}
