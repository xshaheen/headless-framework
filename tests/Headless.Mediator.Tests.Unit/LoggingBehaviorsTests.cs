// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class LoggingBehaviorsTests
{
    [Fact]
    public async Task should_invoke_next_from_request_logging_behavior()
    {
        // given
        var response = new TestResponse();
        var behavior = new RequestLoggingBehavior<TestRequest, TestResponse>(
            new NullCurrentUser(),
            NullLogger<RequestLoggingBehavior<TestRequest, TestResponse>>.Instance
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
            NullLogger<ResponseLoggingBehavior<TestRequest, TestResponse>>.Instance
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
            NullLogger<CriticalRequestLoggingBehavior<TestRequest, TestResponse>>.Instance
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
    public async Task should_log_slow_response_from_critical_request_logging_behavior()
    {
        // given
        var response = new TestResponse();
        var behavior = new CriticalRequestLoggingBehavior<TestRequest, TestResponse>(
            new NullCurrentUser(),
            NullLogger<CriticalRequestLoggingBehavior<TestRequest, TestResponse>>.Instance
        );

        // when
        var result = await behavior.Handle(
            new TestRequest(),
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.1), cancellationToken);

                return response;
            },
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
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
                NullLogger<RequestLoggingBehavior<TestRequest, TestResponse>>.Instance
            );
        var loggerAction = () =>
            new RequestLoggingBehavior<TestRequest, TestResponse>(new NullCurrentUser(), logger: null!);

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
            logger
        );
        var response = new TestResponse();

        // when
        await behavior.Handle(
            new SensitiveRequest(Password: "hunter2"),
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.1), cancellationToken);

                return response;
            },
            CancellationToken.None
        );

        // then: the Warning alert carries type names only; payloads are Debug-only
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(nameof(SensitiveRequest));
        warning.Message.Should().NotContain("hunter2");

        var debug = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Debug).Subject;
        debug.Message.Should().Contain("hunter2");
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
