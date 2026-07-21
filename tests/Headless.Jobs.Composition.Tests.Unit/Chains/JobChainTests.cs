// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Tests.Chains;

/// <summary>
/// Pins the authoring contract for the typed <see cref="JobChain"/> model (issue #311, plan unit U1): payload/descriptor
/// step capture, single-success/single-failure edges, per-step options, structural-depth validation, and immutability
/// after <see cref="JobChainBuilder.Build"/>. Descriptor resolution and persistence belong to later units — this suite
/// only proves the pure authoring/validation logic with no registry or DI access.
/// </summary>
public sealed class JobChainTests
{
    private static JobFunctionDescriptor _Requestless(string name = "cleanup")
    {
        return new JobFunctionDescriptor(
            name,
            requestType: null,
            cronExpression: "",
            JobPriority.Normal,
            maxConcurrency: 0
        );
    }

    [Fact]
    public void start_with_payload_captures_the_payload_and_its_type()
    {
        var payload = new OrderRequest(42);

        var chain = JobChain.Start(payload).Build();

        chain.Root.Payload.Should().BeSameAs(payload);
        chain.Root.PayloadType.Should().Be<OrderRequest>();
        chain.Root.Descriptor.Should().BeNull();
    }

    [Fact]
    public void start_with_descriptor_captures_the_descriptor_and_not_a_payload()
    {
        var descriptor = _Requestless();

        var chain = JobChain.Start(descriptor).Build();

        chain.Root.Descriptor.Should().BeSameAs(descriptor);
        chain.Root.Payload.Should().BeNull();
        chain.Root.PayloadType.Should().BeNull();
    }

    [Fact]
    public void a_root_only_chain_builds_with_no_children()
    {
        var chain = JobChain.Start(new OrderRequest(1)).Build();

        chain.Root.OnSuccess.Should().BeNull();
        chain.Root.OnFailure.Should().BeNull();
    }

    [Fact]
    public void then_maps_to_the_on_success_child_and_catch_maps_to_the_on_failure_child()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new ChargeRequest(2));
        builder.Root.Catch(new RefundRequest(3));

        var chain = builder.Build();

        chain.Root.OnSuccess!.PayloadType.Should().Be<ChargeRequest>();
        chain.Root.OnFailure!.PayloadType.Should().Be<RefundRequest>();
    }

    [Fact]
    public void then_returns_the_child_handle_which_extends_that_branch()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        var charge = builder.Root.Then(new ChargeRequest(2));
        charge.Then(new ReceiptRequest(3));

        var chain = builder.Build();

        chain.Root.OnSuccess!.OnSuccess!.PayloadType.Should().Be<ReceiptRequest>();
    }

    [Fact]
    public void catch_returns_the_child_handle_which_extends_that_branch()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        var refund = builder.Root.Catch(new RefundRequest(2));
        refund.Then(new NotifyRequest(3));

        var chain = builder.Build();

        chain.Root.OnFailure!.OnSuccess!.PayloadType.Should().Be<NotifyRequest>();
    }

    [Fact]
    public void a_second_success_edge_on_the_same_node_throws()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Then(new ChargeRequest(2));

        var act = () => builder.Root.Then(new ReceiptRequest(3));

        act.Should().Throw<InvalidOperationException>().WithMessage("*success*");
    }

    [Fact]
    public void a_second_failure_edge_on_the_same_node_throws()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        builder.Root.Catch(new RefundRequest(2));

        var act = () => builder.Root.Catch(new NotifyRequest(3));

        act.Should().Throw<InvalidOperationException>().WithMessage("*failure*");
    }

    [Fact]
    public void per_step_options_and_execution_time_are_captured_verbatim_on_each_node()
    {
        var rootOptions = new EnqueueOptions { Description = "root", Retries = 1 };
        var childOptions = new EnqueueOptions
        {
            Description = "child",
            Retries = 5,
            RetryIntervals = [1, 2, 3],
            OnNodeDeath = NodeDeathPolicy.Skip,
        };
        var childTime = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var builder = JobChain.Start(new OrderRequest(1), rootOptions);
        builder.Root.Then(new ChargeRequest(2), childOptions, childTime);

        var chain = builder.Build();

        chain.Root.Options.Should().BeSameAs(rootOptions);
        chain.Root.ExecutionTime.Should().BeNull();
        chain.Root.OnSuccess!.Options.Should().BeSameAs(childOptions);
        chain.Root.OnSuccess.ExecutionTime.Should().Be(childTime);
    }

    [Fact]
    public void builds_a_chain_at_the_structural_depth_limit()
    {
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;

        // Root is node 1; adding (limit - 1) Then children makes the longest path exactly the limit.
        for (var i = 1; i < JobChain.MaxStructuralDepth; i++)
        {
            node = node.Then(new OrderRequest(i));
        }

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void throws_when_a_then_chain_exceeds_the_structural_depth()
    {
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;

        for (var i = 1; i <= JobChain.MaxStructuralDepth; i++)
        {
            node = node.Then(new OrderRequest(i));
        }

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{JobChain.MaxStructuralDepth}*");
    }

    [Fact]
    public void catch_branches_count_toward_the_structural_depth()
    {
        var builder = JobChain.Start(new OrderRequest(0));
        var node = builder.Root;

        for (var i = 1; i <= JobChain.MaxStructuralDepth; i++)
        {
            node = node.Catch(new OrderRequest(i));
        }

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{JobChain.MaxStructuralDepth}*");
    }

    [Fact]
    public void a_built_chain_is_immutable_and_further_authoring_throws()
    {
        var builder = JobChain.Start(new OrderRequest(1));
        var charge = builder.Root.Then(new ChargeRequest(2));
        builder.Build();

        var thenAgain = () => charge.Then(new ReceiptRequest(3));
        var catchAgain = () => charge.Catch(new RefundRequest(4));
        var rootCatch = () => builder.Root.Catch(new RefundRequest(5));
        var buildAgain = () => builder.Build();

        thenAgain.Should().Throw<InvalidOperationException>();
        catchAgain.Should().Throw<InvalidOperationException>();
        rootCatch.Should().Throw<InvalidOperationException>();
        buildAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void the_chain_public_surface_exposes_no_handler_contracts()
    {
        var chainTypes = typeof(JobChain)
            .Assembly.GetExportedTypes()
            .Where(t =>
                string.Equals(t.Namespace, "Headless.Jobs", StringComparison.Ordinal)
                && t.Name.StartsWith("JobChain", StringComparison.Ordinal)
            )
            .ToList();

        chainTypes.Should().Contain(typeof(JobChain));
        chainTypes.Should().Contain(typeof(JobChainNode));

        var genericParameterNames = new List<string>();
        var signatureTypeNames = new List<string>();

        foreach (var type in chainTypes)
        {
            genericParameterNames.AddRange(type.GetGenericArguments().Select(a => a.Name));

            var members = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
            );

            foreach (var member in members)
            {
                genericParameterNames.AddRange(member.GetGenericArguments().Select(a => a.Name));
                signatureTypeNames.AddRange(member.GetParameters().Select(p => p.ParameterType.Name));
                signatureTypeNames.Add(member.ReturnType.Name);
            }
        }

        // Payload type parameters (TRequest) are fine; handler-type generics never appear.
        genericParameterNames.Should().NotContain("TJob");
        genericParameterNames.Should().NotContain("TArgs");
        signatureTypeNames.Should().NotContain(name => name.Contains("IJob", StringComparison.Ordinal));
        signatureTypeNames.Should().NotContain(name => name.Contains("ICronJob", StringComparison.Ordinal));
    }

    private sealed record OrderRequest(int Id);

    private sealed record ChargeRequest(int Id);

    private sealed record RefundRequest(int Id);

    private sealed record ReceiptRequest(int Id);

    private sealed record NotifyRequest(int Id);
}
