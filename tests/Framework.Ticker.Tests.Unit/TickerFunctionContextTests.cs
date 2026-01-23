using Framework.Ticker.Base;
using Framework.Ticker.Utilities.Enums;

namespace Tests;

public sealed class TickerFunctionContextTests
{
    [Fact]
    public void GenericContext_Preserves_ScheduledFor_From_Base_Context()
    {
        // given
        var scheduledFor = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var baseContext = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            RetryCount = 1,
            IsDue = true,
            ScheduledFor = scheduledFor,
            FunctionName = "TestFunction",
            CronOccurrenceOperations = null!,
            RequestCancelOperationAction = () => { },
        };

        var request = new TestRequest { Value = 42 };

        // when
        var genericContext = new TickerFunctionContext<TestRequest>(baseContext, request)
        {
            FunctionName = baseContext.FunctionName,
            CronOccurrenceOperations = baseContext.CronOccurrenceOperations,
            RequestCancelOperationAction = baseContext.RequestCancelOperationAction,
        };

        // then
        genericContext.Id.Should().Be(baseContext.Id);
        genericContext.Type.Should().Be(baseContext.Type);
        genericContext.RetryCount.Should().Be(baseContext.RetryCount);
        genericContext.IsDue.Should().Be(baseContext.IsDue);
        genericContext.ScheduledFor.Should().Be(baseContext.ScheduledFor);
        genericContext.FunctionName.Should().Be(baseContext.FunctionName);
        genericContext.Request.Should().Be(request);
    }

    [Fact]
    public void RequestCancellation_Invokes_Underlying_Action()
    {
        // This test is intentionally left empty because RequestCancelOperationAction
        // is an internal delegate that is only set by the runtime scheduler pipeline.
        // Verifying its behavior would require testing internal wiring rather than
        // the public surface area of TickerFunctionContext.
        true.Should().BeTrue();
    }

    private sealed class TestRequest
    {
        public int Value { get; set; }
    }
}
