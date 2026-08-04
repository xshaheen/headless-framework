// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The generated typed-context path calls <see cref="JobsRequestProvider.GetRequestAsync{T}"/> before EVERY typed
/// handler invocation. Swallowing a read failure there invoked the consumer's function with a null request, so an
/// infrastructure fault was recorded either as a misleading <c>NullReferenceException</c> raised by consumer code
/// or — worse — as a succeeded job whose payload was never processed. Both must fail the attempt instead.
/// </summary>
public sealed class JobsRequestProviderTests : TestBase
{
    private sealed record TestRequest(int Value);

    private readonly IInternalJobManager _manager = Substitute.For<IInternalJobManager>();
    private readonly IJobsInstrumentation _instrumentation = Substitute.For<IJobsInstrumentation>();
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly JobFunctionContext _context;

    public JobsRequestProviderTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_manager)
            .AddSingleton(_instrumentation)
            .BuildServiceProvider();
        _scope = _services.CreateAsyncScope();

        _context = new JobFunctionContext
        {
            Id = Guid.NewGuid(),
            Type = JobType.TimeJob,
            FunctionName = "typed-function",
            CronOccurrenceOperations = new CronOccurrenceOperations(() => { }),
        };
        _context.SetServiceScope(_scope);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task get_request_async_returns_the_stored_payload()
    {
        var stored = new TestRequest(7);
        _manager.GetRequestAsync<TestRequest>(_context.Id, _context.Type, AbortToken).Returns(stored);

        (await JobsRequestProvider.GetRequestAsync<TestRequest>(_context, AbortToken)).Should().Be(stored);

        _instrumentation
            .DidNotReceiveWithAnyArgs()
            .LogRequestDeserializationFailure(default!, default!, default, default, default!);
    }

    [Fact]
    public async Task get_request_async_propagates_read_failures_instead_of_yielding_a_default_payload()
    {
        var failure = new InvalidOperationException("payload store unavailable");
        _manager
            .GetRequestAsync<TestRequest>(_context.Id, _context.Type, AbortToken)
            .Returns<TestRequest?>(_ => throw failure);

        var act = () => JobsRequestProvider.GetRequestAsync<TestRequest>(_context, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);

        // Still logged, because this record carries the request type that the generic job-failure record does not.
        _instrumentation
            .Received(1)
            .LogRequestDeserializationFailure(
                typeof(TestRequest).FullName!,
                _context.FunctionName,
                _context.Id,
                _context.Type,
                failure
            );
    }

    [Fact]
    public async Task get_request_async_keeps_cancellation_as_cancellation()
    {
        var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;
        _manager
            .GetRequestAsync<TestRequest>(_context.Id, _context.Type, token)
            .Returns<TestRequest?>(_ => throw new OperationCanceledException(token));

        var act = () => JobsRequestProvider.GetRequestAsync<TestRequest>(_context, token);

        // Cancellation must stay cancellation: the executor's OperationCanceledException arm distinguishes durable
        // cancellation, host shutdown and lease loss, and a deserialization-failure log would misreport all three.
        await act.Should().ThrowAsync<OperationCanceledException>();
        _instrumentation
            .DidNotReceiveWithAnyArgs()
            .LogRequestDeserializationFailure(default!, default!, default, default, default!);

        cancellation.Dispose();
    }
}
