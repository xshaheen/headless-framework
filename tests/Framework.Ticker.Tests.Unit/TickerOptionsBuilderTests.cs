using System.Reflection;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Enums;
using Framework.Ticker.Utilities.Interfaces;
using Framework.Ticker.Utilities.Interfaces.Managers;

namespace Tests;

public class TickerOptionsBuilderTests
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker>;

    private sealed class FakeCronTicker : CronTickerEntity;

    private sealed class FakeExceptionHandler : ITickerExceptionHandler
    {
        public Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType) =>
            Task.CompletedTask;

        public Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType) =>
            Task.CompletedTask;
    }

    [Fact]
    public void ConfigureRequestJsonOptions_Initializes_And_Invokes_Config()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.ConfigureRequestJsonOptions(options =>
        {
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var jsonOptions =
            typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
                .GetProperty(
                    nameof(TickerOptionsBuilder<,>.RequestJsonSerializerOptions),
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .GetValue(builder) as JsonSerializerOptions;

        jsonOptions.Should().NotBeNull();
        jsonOptions.PropertyNameCaseInsensitive.Should().BeTrue();
        jsonOptions.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
    }

    [Fact]
    public void UseGZipCompression_Sets_Flag()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseGZipCompression();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.RequestGZipCompressionEnabled),
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeTrue();
    }

    [Fact]
    public void IgnoreSeedDefinedCronTickers_Disables_Seeding_Flag()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.IgnoreSeedDefinedCronTickers();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.SeedDefinedCronTickers),
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeFalse();
    }

    [Fact]
    public void SetExceptionHandler_Sets_Handler_Type()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.SetExceptionHandler<FakeExceptionHandler>();

        var handlerType =
            typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
                .GetProperty(
                    nameof(TickerOptionsBuilder<,>.TickerExceptionHandlerType),
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .GetValue(builder) as Type;

        handlerType.Should().Be<FakeExceptionHandler>();
    }

    [Fact]
    public void UseTickerSeeder_Time_Sets_TimeSeederAction()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseTickerSeeder(
            async (ITimeTickerManager<FakeTimeTicker> _) =>
            {
                await Task.CompletedTask;
            }
        );

        var seeder = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.TimeSeederAction),
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .GetValue(builder);

        seeder.Should().NotBeNull();
    }

    [Fact]
    public void UseTickerSeeder_Cron_Sets_CronSeederAction()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseTickerSeeder(
            async (ICronTickerManager<FakeCronTicker> _) =>
            {
                await Task.CompletedTask;
            }
        );

        var seeder = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.CronSeederAction),
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .GetValue(builder);

        seeder.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureScheduler_Invokes_Delegate()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.ConfigureScheduler(options =>
        {
            options.MaxConcurrency = 42;
            options.NodeIdentifier = "test-node";
        });

        schedulerOptions.MaxConcurrency.Should().Be(42);
        schedulerOptions.NodeIdentifier.Should().Be("test-node");
    }

    [Fact]
    public void DisableBackgroundServices_Sets_Flag_To_False()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        // Default should be true
        var defaultFlag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.RegisterBackgroundServices),
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);
        defaultFlag.Should().BeOfType<bool>().Which.Should().BeTrue();

        // After calling DisableBackgroundServices, should be false
        builder.DisableBackgroundServices();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty(
                nameof(TickerOptionsBuilder<,>.RegisterBackgroundServices),
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeFalse();
    }
}
