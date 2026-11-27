// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>The module's channels, one per capture store the host registered; an absent store's channel is null.</summary>
internal sealed class DevInboxChannels
{
    public DevInboxChannels(DevInboxStores stores)
    {
        Email = stores.Email is null ? null : new EmailChannel(stores.Email);
        Sms = stores.Sms is null ? null : new SmsChannel(stores.Sms);
        Push = stores.Push is null ? null : new PushChannel(stores.Push);
        All = [.. new IDevInboxChannel?[] { Email, Sms, Push }.OfType<IDevInboxChannel>()];
    }

    public EmailChannel? Email { get; }

    public SmsChannel? Sms { get; }

    public PushChannel? Push { get; }

    /// <summary>The configured channels, in the order email, SMS, push.</summary>
    public IReadOnlyList<IDevInboxChannel> All { get; }

    public IDevInboxChannel? Find(string kind) =>
        All.FirstOrDefault(channel => string.Equals(channel.Kind, kind, StringComparison.Ordinal));
}
