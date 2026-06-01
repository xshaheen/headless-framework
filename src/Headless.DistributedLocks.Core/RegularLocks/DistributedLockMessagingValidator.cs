// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Startup-time hook that detects the registration-order footgun in <c>AddDistributedLock(...)</c>:
/// the <see cref="DistributedLockProvider.LockReleasedConsumer"/> is only wired when an
/// <see cref="IOutboxBus"/> registration exists at the moment <c>AddDistributedLock(...)</c>
/// runs. If the caller registers messaging afterwards (<c>AddMessages(...)</c> later in
/// <c>Program.cs</c>), the consumer is silently skipped and push-based release wake-ups degrade
/// to polling without firing the existing <c>LogOutboxBusAbsent</c> warning (because
/// <c>_outboxBus</c> is non-null at runtime).
/// </summary>
/// <remarks>
/// Implemented as <see cref="IValidateOptions{TOptions}"/> rather than a dedicated
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> because the options pipeline
/// already calls <c>ValidateOnStart()</c> for <see cref="DistributedLockOptions"/> through
/// <c>Headless.Hosting</c>; this piggy-backs on that hook without adding a separate
/// hosted service just to emit one warning. <see cref="Validate"/> always returns
/// <see cref="ValidateOptionsResult.Success"/> — the goal is a warning, not a hard failure
/// (locks still work; they just lose push latency).
/// </remarks>
internal sealed class DistributedLockMessagingValidator(
    IServiceProvider serviceProvider,
    ILogger<DistributedLockProvider> logger
) : IValidateOptions<DistributedLockOptions>
{
    public ValidateOptionsResult Validate(string? name, DistributedLockOptions options)
    {
        if (serviceProvider.GetService<IOutboxBus>() is null)
        {
            // No outbox bus registered — `LogOutboxBusAbsent` already fires from the
            // provider ctor; no additional signal needed here.
            return ValidateOptionsResult.Success;
        }

        var hasConsumer = false;

        foreach (var metadata in serviceProvider.GetServices<ConsumerMetadata>())
        {
            if (metadata.ConsumerType == typeof(DistributedLockProvider.LockReleasedConsumer))
            {
                hasConsumer = true;
                break;
            }
        }

        if (!hasConsumer)
        {
            if (serviceProvider.GetService<DistributedLockReleasedConsumerConflict>() is null)
            {
                logger.LogLockReleasedConsumerMissing();
            }
            else
            {
                logger.LogLockReleasedConsumerConflict();
            }
        }

        return ValidateOptionsResult.Success;
    }
}
