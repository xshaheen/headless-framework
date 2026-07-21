// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute.ExceptionExtensions;

namespace Tests.Chains;

/// <summary>
/// Pins the scheduler's chain enqueue contract (issue #311, plan unit U2): per-node descriptor resolution, the
/// configured <c>MaxChainDepth</c> guard, mapping onto the <see cref="TimeJobEntity"/> tree shape (per-node options,
/// serialized request, <c>RunCondition</c> edges) and the single atomic add into the existing manager path. Joins the
/// serialized <see cref="JobsHelperCollection"/> because the depth-guard tests drive <c>AddHeadlessJobs</c>, which
/// completes the process-global job-function discovery.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class JobChainEnqueueTests : TestBase
{
    private static readonly JobFunctionDescriptor _Order = new(
        "order",
        typeof(OrderRequest),
        "",
        JobPriority.Normal,
        0
    );
    private static readonly JobFunctionDescriptor _Charge = new(
        "charge",
        typeof(ChargeRequest),
        "",
        JobPriority.High,
        0
    );
    private static readonly JobFunctionDescriptor _Refund = new(
        "refund",
        typeof(RefundRequest),
        "",
        JobPriority.Normal,
        0
    );
    private static readonly JobFunctionDescriptor _Receipt = new(
        "receipt",
        typeof(ReceiptRequest),
        "",
        JobPriority.Normal,
        0
    );
    private static readonly JobFunctionDescriptor _Cleanup = new("cleanup", null, "", JobPriority.Normal, 0);

    private static readonly Dictionary<Type, JobFunctionDescriptor> _DescriptorsByType = new()
    {
        [typeof(OrderRequest)] = _Order,
        [typeof(ChargeRequest)] = _Charge,
        [typeof(RefundRequest)] = _Refund,
        [typeof(ReceiptRequest)] = _Receipt,
    };

    private static readonly Dictionary<string, JobFunctionDescriptor> _DescriptorsByName = new(StringComparer.Ordinal)
    {
        ["order"] = _Order,
        ["charge"] = _Charge,
        ["refund"] = _Refund,
        ["receipt"] = _Receipt,
        ["cleanup"] = _Cleanup,
    };

    [Fact]
    public async Task a_chain_deeper_than_the_default_limit_is_rejected_naming_the_configured_limit()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;
        // Root is node 1; ten Then children make the longest path eleven nodes, one past the default limit of ten.
        for (var i = 1; i <= 10; i++)
        {
            node = node.Then(new OrderRequest(i));
        }
        var chain = builder.Build();

        var act = async () => await scheduler.EnqueueAsync(chain, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*maximum chain depth of 10*");
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_lower_configured_limit_rejects_a_shallower_chain_naming_that_limit()
    {
        var (scheduler, timeManager) = _CreateScheduler(maxChainDepth: 3);
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;
        // Root plus three Then children is four nodes deep, one past the configured limit of three.
        for (var i = 1; i <= 3; i++)
        {
            node = node.Then(new OrderRequest(i));
        }
        var chain = builder.Build();

        var act = async () => await scheduler.EnqueueAsync(chain, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*maximum chain depth of 3*");
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_chain_exactly_at_the_configured_limit_is_enqueued()
    {
        var (scheduler, timeManager) = _CreateScheduler(maxChainDepth: 3);
        var adds = _CaptureAdds(timeManager);
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;
        for (var i = 1; i < 3; i++)
        {
            node = node.Then(new OrderRequest(i));
        }
        var chain = builder.Build();

        await scheduler.EnqueueAsync(chain, AbortToken);

        adds.Should().HaveCount(1);
    }

    [Fact]
    public void the_setup_guard_rejects_a_zero_max_chain_depth()
    {
        var act = () =>
            new ServiceCollection().AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler => scheduler.MaxChainDepth = 0)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxChainDepth*");
    }

    [Fact]
    public void the_setup_guard_rejects_a_max_chain_depth_above_the_structural_ceiling()
    {
        var act = () =>
            new ServiceCollection().AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler => scheduler.MaxChainDepth = JobChain.MaxStructuralDepth + 1)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxChainDepth*");
    }

    [Fact]
    public void the_setup_guard_accepts_the_boundary_max_chain_depths()
    {
        var atFloor = () =>
            new ServiceCollection().AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler => scheduler.MaxChainDepth = 1)
            );
        var atCeiling = () =>
            new ServiceCollection().AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler => scheduler.MaxChainDepth = JobChain.MaxStructuralDepth)
            );

        atFloor.Should().NotThrow();
        atCeiling.Should().NotThrow();
    }

    [Fact]
    public async Task an_unmapped_payload_on_a_descendant_throws_before_the_manager_is_called()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new UnknownRequest());
        var chain = builder.Build();

        var act = async () => await scheduler.EnqueueAsync(chain, AbortToken);

        (await act.Should().ThrowAsync<JobFunctionNotFoundException>()).Which.RequestType.Should().Be<UnknownRequest>();
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_requestless_step_naming_a_typed_function_throws_actionably_before_persistence()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var chain = JobChain.Start(_Order).Build();

        var act = async () => await scheduler.EnqueueAsync(chain, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("*typed request overload*");
        await timeManager.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_manager_failure_propagates_after_a_single_add_attempt()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var sentinel = new InvalidOperationException("storage boom");
        timeManager.AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(sentinel);
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new ChargeRequest(2)).Then(new ReceiptRequest(3));
        var chain = builder.Build();

        var act = async () => await scheduler.EnqueueAsync(chain, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
        await timeManager.Received(1).AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task the_whole_tree_is_persisted_through_a_single_add_call()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var adds = _CaptureAdds(timeManager);
        var builder = JobChain.Start(new OrderRequest(1));
        var charge = builder.Root.Then(new ChargeRequest(2));
        charge.Then(new ReceiptRequest(3));
        charge.Catch(new RefundRequest(4));
        var chain = builder.Build();

        await scheduler.EnqueueAsync(chain, AbortToken);

        adds.Should().HaveCount(1);
        var root = adds[0];
        root.Function.Should().Be("order");
        root.Children.Should().HaveCount(1);
        var chargeEntity = root.Children.Single();
        chargeEntity.Function.Should().Be("charge");
        chargeEntity.Children.Should().HaveCount(2);
    }

    [Fact]
    public async Task then_maps_to_on_success_and_catch_maps_to_on_failure_with_a_null_root_condition()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var adds = _CaptureAdds(timeManager);
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new ChargeRequest(2));
        builder.Root.Catch(new RefundRequest(3));
        var chain = builder.Build();

        await scheduler.EnqueueAsync(chain, AbortToken);

        var root = adds.Single();
        root.RunCondition.Should().BeNull();
        var onSuccess = root.Children.Single(child => child.RunCondition == RunCondition.OnSuccess);
        var onFailure = root.Children.Single(child => child.RunCondition == RunCondition.OnFailure);
        onSuccess.Function.Should().Be("charge");
        onFailure.Function.Should().Be("refund");
    }

    [Fact]
    public async Task re_enqueuing_the_same_built_chain_produces_independent_trees()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var adds = _CaptureAdds(timeManager);
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new ChargeRequest(2));
        var chain = builder.Build();

        var firstId = await scheduler.EnqueueAsync(chain, AbortToken);
        var secondId = await scheduler.EnqueueAsync(chain, AbortToken);

        adds.Should().HaveCount(2);
        adds[0].Should().NotBeSameAs(adds[1]);
        adds[0].Children.Single().Should().NotBeSameAs(adds[1].Children.Single());
        firstId.Should().Be(adds[0].Id);
        secondId.Should().Be(adds[1].Id);
        firstId.Should().NotBe(secondId);
    }

    [Fact]
    public async Task per_node_options_execution_time_and_payloads_land_on_the_matching_entities()
    {
        var (scheduler, timeManager) = _CreateScheduler();
        var adds = _CaptureAdds(timeManager);
        var rootOptions = new EnqueueOptions { Description = "place order", Retries = 1 };
        var chargeOptions = new EnqueueOptions
        {
            Description = "charge card",
            Retries = 4,
            RetryIntervals = [5, 10],
            OnNodeDeath = NodeDeathPolicy.MarkFailed,
        };
        var chargeTime = new DateTime(2030, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var orderRequest = new OrderRequest(7);
        var builder = JobChain.Start(orderRequest, rootOptions);
        builder.Root.Then(new ChargeRequest(8), chargeOptions, chargeTime);
        builder.Root.Catch(_Cleanup);
        var chain = builder.Build();

        await scheduler.EnqueueAsync(chain, AbortToken);

        var root = adds.Single();
        root.Function.Should().Be("order");
        root.Description.Should().Be("place order");
        root.Retries.Should().Be(1);
        root.RetryIntervals.Should().BeNull();
        root.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
        root.ExecutionTime.Should().BeNull();
        JobsHelper
            .ReadJobRequest<OrderRequest>(root.Request!, JobsRequestSerializationOptions.Default)
            .Should()
            .Be(orderRequest);

        var charge = root.Children.Single(child => child.RunCondition == RunCondition.OnSuccess);
        charge.Description.Should().Be("charge card");
        charge.Retries.Should().Be(4);
        charge.RetryIntervals.Should().Equal(5, 10);
        charge.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        charge.ExecutionTime.Should().Be(chargeTime);
        charge.Request.Should().NotBeNull();

        var cleanup = root.Children.Single(child => child.RunCondition == RunCondition.OnFailure);
        cleanup.Function.Should().Be("cleanup");
        cleanup.Request.Should().BeNull();
        cleanup.Retries.Should().Be(0);
        cleanup.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public void chain_step_options_expose_no_per_step_priority()
    {
        typeof(EnqueueOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Should()
            .NotContain("Priority");
    }

    private static (IJobScheduler Scheduler, ITimeJobManager<TimeJobEntity> TimeManager) _CreateScheduler(
        int maxChainDepth = SchedulerOptionsBuilder.DefaultMaxChainDepth,
        JobsRequestSerializationOptions? serializationOptions = null
    )
    {
        var timeManager = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cronManager = Substitute.For<ICronJobManager<CronJobEntity>>();
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            timeManager,
            cronManager,
            _DescriptorsByType.GetValueOrDefault,
            _DescriptorsByName.GetValueOrDefault,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>(),
            serializationOptions: serializationOptions,
            maxChainDepth: maxChainDepth
        );

        return (scheduler, timeManager);
    }

    private static List<TimeJobEntity> _CaptureAdds(ITimeJobManager<TimeJobEntity> timeManager)
    {
        var captured = new List<TimeJobEntity>();
        timeManager
            .AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = call.Arg<TimeJobEntity>();
                entity.Id = Guid.NewGuid();
                captured.Add(entity);
                return Task.FromResult(entity);
            });

        return captured;
    }

    private sealed record OrderRequest(int Id);

    private sealed record ChargeRequest(int Id);

    private sealed record RefundRequest(int Id);

    private sealed record ReceiptRequest(int Id);

    private sealed record UnknownRequest;
}
