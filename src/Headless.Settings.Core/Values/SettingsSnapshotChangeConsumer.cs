// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

namespace Headless.Settings.Values;

/// <summary>
/// Reloads the settings snapshots a <see cref="SettingChangedMessage"/> concerns. One consumer serves every snapshot the
/// host registered with <c>AddSettingsSnapshot</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every-instance: each snapshot lives in one process, so every process must see every announcement instead of sharing
/// one copy with its replicas. The consumer never filters on <see cref="SettingChangedMessage.OriginHostName"/>: the
/// writing process holds snapshots too, and the manager that stored the value knows nothing about them.
/// </para>
/// <para>
/// Delivery is at most once, so each time the subscription is established, first or after a gap, every loaded snapshot
/// is reloaded. The first establishment counts too: a snapshot loaded before the subscription went live missed any
/// announcement published in between. A snapshot still loading is skipped, so host startup, which waits for this hook,
/// never waits on the settings store.
/// </para>
/// </remarks>
[BusConsumer(Identity, EveryInstance = true)]
internal sealed class SettingsSnapshotChangeConsumer(IEnumerable<ISettingsSnapshotEntry> snapshots)
    : IConsume<SettingChangedMessage>,
        IOnSubscriptionEstablished
{
    /// <summary>The consumer identity that <c>Tune</c> refers to.</summary>
    public const string Identity = "headless.settings.snapshot";

    public async ValueTask ConsumeAsync(
        ConsumeContext<SettingChangedMessage> context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);

        var message = context.Message;

        // A tenant or user override never changes the Global value a snapshot is bound from.
        if (!string.Equals(message.ProviderName, SettingValueProviderNames.Global, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var snapshot in snapshots)
        {
            string[] announced = [.. message.SettingNames.Where(snapshot.Names.Contains)];

            if (announced.Length != 0)
            {
                // Reload failures are logged inside the snapshot; one snapshot's failure does not skip the others.
                await snapshot
                    .ReloadAsync(SettingsSnapshotReloadReason.Message, announced, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public async ValueTask OnSubscriptionEstablishedAsync(
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);

        foreach (var snapshot in snapshots)
        {
            await snapshot
                .ReloadAsync(SettingsSnapshotReloadReason.Establishment, announcedNames: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
