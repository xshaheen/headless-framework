// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Startup-time hook that detects when messaging is available but the caller did not opt into
/// <see cref="DistributedLockProvider.LockReleasedConsumer"/> through
/// <c>setup.UseDistributedLockReleaseWakeups()</c>. Push-based release wake-ups then degrade to
/// polling without firing the existing <c>LogOutboxBusAbsent</c> warning (because
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

        var hasConsumer =
            serviceProvider
                .GetService<IConsumerRegistry>()
                ?.GetAll()
                .Any(static metadata => metadata.ConsumerType == typeof(DistributedLockProvider.LockReleasedConsumer))
            == true;

        if (!hasConsumer)
        {
            logger.LogLockReleasedConsumerMissing();
        }

        return ValidateOptionsResult.Success;
    }
}
