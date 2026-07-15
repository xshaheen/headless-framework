// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Communication.Email;
using Headless.Emails;
using Headless.Emails.Azure;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class AzureCommunicationEmailSenderTests : TestBase
{
    private const string _SenderAddress = "sender@example.com";
    private const string _RecipientAddress = "recipient@example.com";

    private readonly CapturingLogger<AzureCommunicationEmailSender> _logger = new();

    [Fact]
    public async Task should_return_succeeded_when_operation_completes_with_succeeded_status()
    {
        // given
        var operation = _OperationWith(EmailSendStatus.Succeeded);
        var sender = new AzureCommunicationEmailSender(new FakeEmailClient(_ => operation), _logger);

        // when
        var response = await sender.SendAsync(_Request(), AbortToken);

        // then
        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().Be("op-123");
        _logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_failed_with_the_terminal_status_when_operation_completes_with_non_success_status()
    {
        // given
        var operation = _OperationWith(EmailSendStatus.Failed, operationId: "op-failed");
        var sender = new AzureCommunicationEmailSender(new FakeEmailClient(_ => operation), _logger);

        // when
        var response = await sender.SendAsync(_Request(), AbortToken);

        // then - the response surfaces the terminal status (parity with the exception paths); the no-PII
        // invariant is asserted on the LOG path, not the response.
        response.Success.Should().BeFalse();
        response.FailureError.Should().Contain("Failed");
        _logger.Messages.Should().NotBeEmpty();
        _AssertLogsHaveNoAddress();
    }

    [Fact]
    public async Task should_return_failed_with_the_provider_detail_when_request_failed_exception_is_thrown()
    {
        // given
        var exception = new RequestFailedException(400, "Bad Request", "EmailRejected", innerException: null);
        var sender = new AzureCommunicationEmailSender(new FakeEmailClient(_ => throw exception), _logger);

        // when
        var response = await sender.SendAsync(_Request(), AbortToken);

        // then - the caller receives the provider's raw rejection reason (parity with SES/Mailkit); the
        // no-PII invariant is asserted on the LOG path, not the response.
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be(exception.Message);
        _logger.Messages.Should().NotBeEmpty();
        _AssertLogsHaveNoAddress();
    }

    [Fact]
    public async Task should_return_failed_when_a_non_request_failed_transport_fault_is_thrown()
    {
        // given - a transport/SDK fault that is neither a RequestFailedException nor a caller cancellation.
        var exception = new HttpRequestException("connection reset");
        var sender = new AzureCommunicationEmailSender(new FakeEmailClient(_ => throw exception), _logger);

        // when
        var response = await sender.SendAsync(_Request(), AbortToken);

        // then - it is returned as a failed response rather than thrown; the log records only the type.
        response.Success.Should().BeFalse();
        response.FailureError.Should().Be(exception.Message);
        _logger.Messages.Should().NotBeEmpty();
        _AssertLogsHaveNoAddress();
    }

    [Fact]
    public async Task should_propagate_caller_cancellation()
    {
        // given - the caller's own token is cancelled and ACS surfaces an OperationCanceledException.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sender = new AzureCommunicationEmailSender(
            new FakeEmailClient(_ => throw new OperationCanceledException(cts.Token)),
            _logger
        );

        // when
        var action = async () => await sender.SendAsync(_Request(), cts.Token);

        // then - only the caller's own cancellation propagates.
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_return_failed_when_operation_canceled_without_caller_cancellation()
    {
        // given - an OperationCanceledException raised while the caller's token is NOT cancelled.
        var sender = new AzureCommunicationEmailSender(
            new FakeEmailClient(_ => throw new OperationCanceledException()),
            _logger
        );

        // when
        var response = await sender.SendAsync(_Request(), AbortToken);

        // then - it is a delivery failure, not a caller cancellation, so it is returned rather than thrown.
        response.Success.Should().BeFalse();
        response.FailureError.Should().NotBeNull();
    }

    private void _AssertLogsHaveNoAddress()
    {
        foreach (var message in _logger.Messages)
        {
            message.Should().NotContain(_RecipientAddress).And.NotContain(_SenderAddress);
        }
    }

    private static EmailSendOperation _OperationWith(EmailSendStatus status, string operationId = "op-123")
    {
        var result = EmailModelFactory.EmailSendResult(operationId, status);
        var operation = Substitute.For<EmailSendOperation>();
        operation.Value.Returns(result);
        operation.Id.Returns(operationId);

        return operation;
    }

    private static SendSingleEmailRequest _Request()
    {
        return new SendSingleEmailRequest
        {
            From = new EmailRequestAddress(_SenderAddress),
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress(_RecipientAddress)] },
            Subject = "Subject",
            MessageText = "body",
        };
    }

    private sealed class FakeEmailClient(Func<EmailMessage, EmailSendOperation> onSend) : EmailClient
    {
        public override Task<EmailSendOperation> SendAsync(
            WaitUntil waitUntil,
            EmailMessage message,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(onSend(message));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
