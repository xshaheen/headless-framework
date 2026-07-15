// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Jobs;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// U1 wiring: the Jobs-scoped keyed lock registration and the <c>UseStorageLock</c> flag. Provider-agnostic, so these
/// live in the unit project (KTD8). Uses a <see cref="NullDistributedLock"/> as the supplied provider — wiring tests
/// care only about which instance lands under the keyed slot, not lock behavior.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class JobsDistributedLockWiringTests
{
    private static int _CountJobsLockDescriptors(IServiceCollection services)
    {
        return services.Count(d =>
            d.ServiceType == typeof(IDistributedLock)
            && d.IsKeyedService
            && string.Equals(d.ServiceKey as string, JobsKeys.LockProvider, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void use_distributed_lock_instance_registers_provider_and_enables_flag()
    {
        var provider = new NullDistributedLock(TimeProvider.System);

        var services = new ServiceCollection();
        services.AddHeadlessJobs(o => o.UseDistributedLock(provider));

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeSameAs(provider);
        sp.GetRequiredService<SchedulerOptionsBuilder>().UseStorageLock.Should().BeTrue();
    }

    [Fact]
    public void use_distributed_lock_factory_registers_factory_output_and_enables_flag()
    {
        var provider = new NullDistributedLock(TimeProvider.System);

        var services = new ServiceCollection();
        services.AddHeadlessJobs(o => o.UseDistributedLock(_ => provider));

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeSameAs(provider);
        sp.GetRequiredService<SchedulerOptionsBuilder>().UseStorageLock.Should().BeTrue();
    }

    [Fact]
    public void default_registration_resolves_null_lock_and_flag_is_false()
    {
        var services = new ServiceCollection();
        services.AddHeadlessJobs();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeOfType<NullDistributedLock>();
        sp.GetRequiredService<SchedulerOptionsBuilder>().UseStorageLock.Should().BeFalse();
    }

    [Fact]
    public void use_distributed_lock_called_twice_keeps_only_the_last_registration()
    {
        var first = new NullDistributedLock(TimeProvider.System);
        var second = new NullDistributedLock(TimeProvider.System);

        var services = new ServiceCollection();
        services.AddHeadlessJobs(o =>
        {
            o.UseDistributedLock(first);
            o.UseDistributedLock(second);
        });

        // Exactly one keyed slot (the last-wins provider); the Null fallback is suppressed by TryAdd.
        _CountJobsLockDescriptors(services).Should().Be(1);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeSameAs(second);
    }

    [Fact]
    public void factory_then_instance_keeps_only_the_instance()
    {
        var factoryOutput = new NullDistributedLock(TimeProvider.System);
        var instance = new NullDistributedLock(TimeProvider.System);

        var services = new ServiceCollection();
        services.AddHeadlessJobs(o =>
        {
            o.UseDistributedLock(_ => factoryOutput);
            o.UseDistributedLock(instance);
        });

        _CountJobsLockDescriptors(services).Should().Be(1);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeSameAs(instance);
    }

    [Fact]
    public void instance_then_factory_keeps_only_the_factory()
    {
        var instance = new NullDistributedLock(TimeProvider.System);
        var factoryOutput = new NullDistributedLock(TimeProvider.System);

        var services = new ServiceCollection();
        services.AddHeadlessJobs(o =>
        {
            o.UseDistributedLock(instance);
            o.UseDistributedLock(_ => factoryOutput);
        });

        _CountJobsLockDescriptors(services).Should().Be(1);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider).Should().BeSameAs(factoryOutput);
    }
}
