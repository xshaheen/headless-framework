// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class LoggingBehaviorsTests
{
    private static readonly MediatorPayloadLogging _PayloadsOff = new(Enabled: false);
    private static readonly MediatorPayloadLogging _PayloadsOn = new(Enabled: true);

    [Fact]
    public async Task should_invoke_next_from_request_logging_behavior()
    {
        // given
        var response = new TestResponse();
        var behavior = new RequestLoggingBehavior<TestRequest, TestResponse>(
            new NullCurrentUser(),
            NullLogger<RequestLoggingBehavior<TestRequest, TestResponse>>.Instance,
            _PayloadsOff
        );
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(),
            _CreateNext(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_next_from_response_logging_behavior()
    {
        // given
        var response = new TestResponse();
        var behavior = new ResponseLoggingBehavior<TestRequest, TestResponse>(
            new NullCurrentUser(),
            NullLogger<ResponseLoggingBehavior<TestRequest, TestResponse>>.Instance,
            _PayloadsOff
        );
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(),
            _CreateNext(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_next_from_critical_request_logging_behavior()
    {
        // given
        var response = new TestResponse();
        var behavior = new CriticalRequestLoggingBehavior<TestRequest, TestResponse>(
            new NullCurrentUser(),
            NullLogger<CriticalRequestLoggingBehavior<TestRequest, TestResponse>>.Instance,
            _PayloadsOff
        );
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(),
            _CreateNext(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_logging_behavior_dependencies_are_null()
    {
        // given
        ICurrentUser? currentUser = null;

        // when
        var currentUserAction = () =>
            new RequestLoggingBehavior<TestRequest, TestResponse>(
                currentUser!,
                NullLogger<RequestLoggingBehavior<TestRequest, TestResponse>>.Instance,
                _PayloadsOff
            );
        var loggerAction = () =>
            new RequestLoggingBehavior<TestRequest, TestResponse>(new NullCurrentUser(), logger: null!, _PayloadsOff);

        // then
        currentUserAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(currentUser));
        loggerAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task should_not_log_payload_at_warning_from_critical_request_logging_behavior()
    {
        // given
        var logger = new CapturingLogger<CriticalRequestLoggingBehavior<SensitiveRequest, TestResponse>>();
        var behavior = new CriticalRequestLoggingBehavior<SensitiveRequest, TestResponse>(
            new NullCurrentUser(),
            logger,
            _PayloadsOn
        );
        var response = new TestResponse();

        // when
        var result = await behavior.Handle(
            new SensitiveRequest(Password: "hunter2"),
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.1), cancellationToken);

                return response;
            },
            CancellationToken.None
        );

        // then: the Warning alert carries type names only; payloads are Debug-only
        result.Should().BeSameAs(response);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(nameof(SensitiveRequest));
        warning.Message.Should().NotContain("hunter2");

        var debug = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Debug).Subject;
        debug.Message.Should().Contain("hunter2");
    }

    [Fact]
    public async Task should_log_no_payload_from_slow_request_behavior_when_payloads_are_off()
    {
        // given
        var logger = new CapturingLogger<CriticalRequestLoggingBehavior<SensitiveRequest, TestResponse>>();
        var behavior = new CriticalRequestLoggingBehavior<SensitiveRequest, TestResponse>(
            new NullCurrentUser(),
            logger,
            _PayloadsOff
        );

        // when
        await behavior.Handle(
            new SensitiveRequest(Password: "hunter2"),
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.1), cancellationToken);

                return new TestResponse();
            },
            CancellationToken.None
        );

        // then: the Warning alert still fires, and no entry carries the payload
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        logger.Entries.Should().NotContain(e => e.Message.Contains("hunter2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_log_request_payload_only_when_payloads_are_on(bool logPayloads)
    {
        // given
        var logger = new CapturingLogger<RequestLoggingBehavior<SensitiveRequest, TestResponse>>();
        var behavior = new RequestLoggingBehavior<SensitiveRequest, TestResponse>(
            new NullCurrentUser(),
            logger,
            new MediatorPayloadLogging(logPayloads)
        );

        // when
        await behavior.Handle(
            new SensitiveRequest(Password: "hunter2"),
            _CreateNext<SensitiveRequest>(),
            CancellationToken.None
        );

        // then: the entry always names the message, and carries its content only when asked
        var entry = logger.Entries.Should().ContainSingle(e => e.EventId.Id == 1).Subject;
        entry.Message.Should().Contain(nameof(SensitiveRequest));
        entry.Message.Contains("hunter2", StringComparison.Ordinal).Should().Be(logPayloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_log_response_payload_only_when_payloads_are_on(bool logPayloads)
    {
        // given
        var logger = new CapturingLogger<ResponseLoggingBehavior<SensitiveRequest, TestResponse>>();
        var behavior = new ResponseLoggingBehavior<SensitiveRequest, TestResponse>(
            new NullCurrentUser(),
            logger,
            new MediatorPayloadLogging(logPayloads)
        );

        // when
        await behavior.Handle(
            new SensitiveRequest(Password: "hunter2"),
            _CreateNext<SensitiveRequest>(),
            CancellationToken.None
        );

        // then
        var entry = logger.Entries.Should().ContainSingle(e => e.EventId.Id == 2).Subject;
        entry.Message.Should().Contain(nameof(SensitiveRequest)).And.Contain(nameof(TestResponse));
        entry.Message.Contains("hunter2", StringComparison.Ordinal).Should().Be(logPayloads);
    }

    private static MessageHandlerDelegate<TRequest, TestResponse> _CreateNext<TRequest>()
        where TRequest : IRequest<TestResponse>
    {
        return (_, _) => new ValueTask<TestResponse>(new TestResponse());
    }

    private static MessageHandlerDelegate<TestRequest, TestResponse> _CreateNext(TestResponse response, Action onInvoke)
    {
        return (_, _) =>
        {
            onInvoke();

            return new ValueTask<TestResponse>(response);
        };
    }

    private sealed record TestRequest : IRequest<TestResponse>;

    private sealed record TestResponse;

    private sealed record SensitiveRequest(string Password) : IRequest<TestResponse>;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

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
            Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}
