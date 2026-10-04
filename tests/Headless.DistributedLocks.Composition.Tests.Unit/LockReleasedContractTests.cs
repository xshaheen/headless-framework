// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The release signal travels under the name the lock providers declare, so replicas and services sharing a broker
/// agree on the topic whatever naming conventions each host configures for its own messages.
/// </summary>
public sealed class LockReleasedContractTests : TestBase
{
    [Fact]
    public async Task should_publish_and_consume_under_the_declared_name_whatever_the_host_conventions()
    {
        // given - conventions that rename every message the host does not declare
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                    setup.UseConventions(static conventions =>
                        conventions
                            .UseKebabCaseMessageNames()
                            .WithMessageNamePrefix("app.")
                            .WithMessageNameSuffix(".event")
                    );
                });
            },
            AbortToken
        );
        var locks = harness.ServiceProvider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var lease = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        lease.Should().NotBeNull();

        // when - the lock's own release path publishes the signal
        await locks.ReleaseAsync(resource, lease!.LeaseId, AbortToken);

        // then
        var published = await harness.WaitForPublished<DistributedLockReleased>(cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumed<DistributedLockReleased>(cancellationToken: AbortToken);
        published.MessageName.Should().Be("headless.locks.released");
        consumed.MessageName.Should().Be("headless.locks.released");
        harness
            .ServiceProvider.GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle()
            .Which.MessageContractVersion.Should()
            .Be("1");
    }
}
