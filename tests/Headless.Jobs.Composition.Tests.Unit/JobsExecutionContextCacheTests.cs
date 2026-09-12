// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// U3/R6: <see cref="JobsExecutionContext.CacheFunctionReferences(JobExecutionState, JobFunctionRegistry)"/> must
/// stamp the cached delegate/priority/max-concurrency onto EVERY node of a hydrated chain — not just the first two
/// levels — so a chain deeper than a grandchild, or a branching one, executes its whole tail with the right per-node
/// scheduling knobs after a fallback pickup.
/// </summary>
public sealed class JobsExecutionContextCacheTests : TestBase
{
    [Fact]
    public void unsupported_version_cannot_reach_a_previously_cached_delegate()
    {
        var registration = _Fn("stable", JobPriority.Normal, 0);
        var registry = JobFunctionRegistryBuilder.Build(
            [registration],
            [],
            [
                new KeyValuePair<string, JobFunctionDescriptor>(
                    "stable",
                    new("stable", null, "", JobPriority.Normal, 0, "2")
                ),
            ]
        );
        var context = _Node("stable");
        context.ContractVersion = "1";
        context.CachedDelegate = registration.Value.Delegate;

        JobsExecutionContext.CacheFunctionReferences(context, registry);

        context.CachedDelegate.Should().BeNull();
        context
            .ContractVersionError.Should()
            .Contain("version '1'")
            .And.Contain("registers '2'")
            .And.Contain("not deserialized");
    }

    [Fact]
    public void caches_function_references_across_a_deep_branching_tree()
    {
        var registry = JobFunctionRegistryBuilder.Build(
            [_Fn("alpha", JobPriority.High, maxConcurrency: 3), _Fn("beta", JobPriority.Low, maxConcurrency: 7)],
            [],
            []
        );

        // A tree that is both deep (root -> a -> a -> a, four levels) and branching (each node also carries a 'beta'
        // sibling subtree), so a two-level cap or a non-recursive branch would leave some node unstamped.
        var root = _Node("alpha");
        var deep = root;
        for (var level = 0; level < 3; level++)
        {
            var next = _Node("alpha");
            next.TimeJobChildren.Add(_Node("beta")); // a branch at every level
            deep.TimeJobChildren.Add(next);
            deep = next;
        }

        JobsExecutionContext.CacheFunctionReferences(root, registry);

        foreach (var node in _Flatten(root))
        {
            node.CachedDelegate.Should().NotBeNull("every node in the tree must be stamped, node {0}", node.JobId);
            if (string.Equals(node.FunctionName, "alpha", StringComparison.Ordinal))
            {
                node.CachedPriority.Should().Be(JobPriority.High);
                node.CachedMaxConcurrency.Should().Be(3);
            }
            else
            {
                node.CachedPriority.Should().Be(JobPriority.Low);
                node.CachedMaxConcurrency.Should().Be(7);
            }
        }
    }

    private static KeyValuePair<string, JobFunctionRegistration> _Fn(
        string name,
        JobPriority priority,
        int maxConcurrency
    ) =>
        new(
            name,
            new JobFunctionRegistration
            {
                CronExpression = "",
                Priority = priority,
                MaxConcurrency = maxConcurrency,
                Delegate = (_, _, _) => Task.CompletedTask,
            }
        );

    private static JobExecutionState _Node(string functionName) =>
        new()
        {
            JobId = Guid.NewGuid(),
            FunctionName = functionName,
            Type = JobType.TimeJob,
            Status = JobStatus.Queued,
        };

    private static IEnumerable<JobExecutionState> _Flatten(JobExecutionState node)
    {
        yield return node;
        foreach (var child in node.TimeJobChildren)
        {
            foreach (var descendant in _Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
