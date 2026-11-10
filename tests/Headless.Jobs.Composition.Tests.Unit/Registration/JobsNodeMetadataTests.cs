// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Registration;

/// <summary>
/// A node on the coordinated durable path publishes the functions its run filter lets it claim in its Coordination node
/// metadata, so a dashboard can name a function no live node runs.
/// </summary>
public sealed class JobsNodeMetadataTests : TestBase
{
    [Fact]
    public void should_list_the_functions_the_run_filter_claims()
    {
        var metadata = _Metadata(jobs => jobs.RunOnly("billing.*"));

        JobsNodeMetadata.TryReadClaimedFunctions(metadata, out var functions).Should().BeTrue();
        functions.Should().Equal(TestJobs.BillingCloseDay, TestJobs.BillingSendInvoice);
    }

    [Fact]
    public void should_list_every_registered_function_on_an_unfiltered_node()
    {
        var metadata = _Metadata(_ => { });

        JobsNodeMetadata.TryReadClaimedFunctions(metadata, out var functions).Should().BeTrue();
        functions.Should().Equal(TestJobs.BillingCloseDay, TestJobs.BillingSendInvoice, TestJobs.OrdersShip);
    }

    [Fact]
    public void should_list_no_function_on_a_node_that_claims_no_work()
    {
        var metadata = _Metadata(jobs => jobs.DisableBackgroundServices().DisableStartupInitialization());

        JobsNodeMetadata.TryReadClaimedFunctions(metadata, out var functions).Should().BeTrue();
        functions.Should().BeEmpty();
    }

    [Fact]
    public void should_read_a_node_without_the_entry_or_with_a_malformed_one_as_unpublished()
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [JobsNodeMetadata.ClaimedFunctionsKey] = "{not json",
        };

        JobsNodeMetadata.TryReadClaimedFunctions(empty, out var none).Should().BeFalse();
        none.Should().BeEmpty();
        JobsNodeMetadata.TryReadClaimedFunctions(malformed, out _).Should().BeFalse();
    }

    [Fact]
    public void should_publish_the_host_max_concurrency()
    {
        var metadata = _Metadata(jobs => jobs.ConfigureScheduler(scheduler => scheduler.MaxConcurrency = 7));

        metadata[JobsNodeMetadata.MaxConcurrencyKey].Should().Be("7");
        JobsNodeMetadata.ReadMaxConcurrency(metadata).Should().Be(7);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("seven")]
    [InlineData("-1")]
    public void should_read_a_missing_or_malformed_max_concurrency_as_unpublished(string? value)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (value is not null)
        {
            metadata[JobsNodeMetadata.MaxConcurrencyKey] = value;
        }

        JobsNodeMetadata.ReadMaxConcurrency(metadata).Should().BeNull();
    }

    private static IReadOnlyDictionary<string, string> _Metadata(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<INodeMembership>());
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
        {
            jobs.AddModule<BillingJobsModule>().AddModule<OrdersJobsModule>();
            configure(jobs);
            jobs.UseEntityFramework();
        });
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<CoordinationOptions>>().Value.Metadata;
    }
}
