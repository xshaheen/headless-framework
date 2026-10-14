// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class ValidationRequestPreProcessorTests
{
    [Fact]
    public async Task should_invoke_next_when_no_validators_are_registered()
    {
        // given
        var response = new TestResponse();
        var behavior = new ValidationRequestPreProcessor<TestRequest, TestResponse>(
            [],
            NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
        );
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(ProductId: ""),
            _CreateNext(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_next_when_validators_return_no_failures()
    {
        // given
        var response = new TestResponse();
        var behavior = new ValidationRequestPreProcessor<TestRequest, TestResponse>(
            [new TestRequestValidator()],
            NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
        );
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(ProductId: "sku-1"),
            _CreateNext(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_validation_exception_when_any_validator_returns_failures()
    {
        // given
        var response = new TestResponse();
        var behavior = new ValidationRequestPreProcessor<TestRequest, TestResponse>(
            [new TestRequestValidator()],
            NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
        );
        var callCount = 0;

        // when
        var action = async () =>
            await behavior.Handle(
                new TestRequest(ProductId: ""),
                _CreateNext(response, () => callCount++),
                CancellationToken.None
            );

        // then
        var exception = await action.Should().ThrowExactlyAsync<FluentValidation.ValidationException>();
        exception.Which.Errors.Should().ContainSingle(failure => failure.PropertyName == nameof(TestRequest.ProductId));
        callCount.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_validation_exception_with_all_validator_failures()
    {
        // given
        var response = new TestResponse();
        var behavior = new ValidationRequestPreProcessor<TestRequest, TestResponse>(
            [new TestRequestValidator(), new SecondTestRequestValidator()],
            NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
        );
        var callCount = 0;

        // when
        var action = async () =>
            await behavior.Handle(
                new TestRequest(ProductId: ""),
                _CreateNext(response, () => callCount++),
                CancellationToken.None
            );

        // then
        var exception = await action.Should().ThrowExactlyAsync<FluentValidation.ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
        callCount.Should().Be(0);
    }

    [Fact]
    public async Task should_run_multiple_async_validators_one_at_a_time_in_registration_order()
    {
        // given
        var probe = new OverlapProbe();
        var behavior = new ValidationRequestPreProcessor<TestRequest, TestResponse>(
            [new SlowAsyncValidator(probe, "first"), new SlowAsyncValidator(probe, "second")],
            NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
        );

        // when
        await behavior.Handle(
            new TestRequest(ProductId: "sku-1"),
            _CreateNext(new TestResponse(), () => { }),
            CancellationToken.None
        );

        // then
        probe.MaxInFlight.Should().Be(1);
        probe.Order.Should().Equal("first", "second");
    }

    [Fact]
    public void should_throw_argument_null_exception_when_validators_are_null()
    {
        // given
        IEnumerable<IValidator<TestRequest>>? validators = null;

        // when
        var action = () =>
            new ValidationRequestPreProcessor<TestRequest, TestResponse>(
                validators!,
                NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>.Instance
            );

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(validators));
    }

    [Fact]
    public void should_throw_argument_null_exception_when_logger_is_null()
    {
        // given
        NullLogger<ValidationRequestPreProcessor<TestRequest, TestResponse>>? logger = null;

        // when
        var action = () => new ValidationRequestPreProcessor<TestRequest, TestResponse>([], logger!);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(logger));
    }

    private static MessageHandlerDelegate<TestRequest, TestResponse> _CreateNext(TestResponse response, Action onInvoke)
    {
        return (_, _) =>
        {
            onInvoke();

            return new ValueTask<TestResponse>(response);
        };
    }

    private sealed record TestRequest(string ProductId) : IRequest<TestResponse>;

    private sealed record TestResponse;

    private sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(request => request.ProductId).NotEmpty();
        }
    }

    private sealed class OverlapProbe
    {
        private int _inFlight;
        private int _maxInFlight;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _order = new();

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public IReadOnlyList<string> Order => [.. _order];

        public async Task<bool> RunAsync(string name, CancellationToken cancellationToken)
        {
            _order.Enqueue(name);
            var current = Interlocked.Increment(ref _inFlight);
            int observed;

            do
            {
                observed = Volatile.Read(ref _maxInFlight);
            } while (
                current > observed && Interlocked.CompareExchange(ref _maxInFlight, current, observed) != observed
            );

            // Long enough that two validators started together are certain to overlap.
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            Interlocked.Decrement(ref _inFlight);

            return true;
        }
    }

    private sealed class SlowAsyncValidator : AbstractValidator<TestRequest>
    {
        public SlowAsyncValidator(OverlapProbe probe, string name)
        {
            RuleFor(request => request.ProductId).MustAsync((_, ct) => probe.RunAsync(name, ct));
        }
    }

    private sealed class SecondTestRequestValidator : AbstractValidator<TestRequest>
    {
        public SecondTestRequestValidator()
        {
            RuleFor(request => request.ProductId).MinimumLength(3);
        }
    }
}
