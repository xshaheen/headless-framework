// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DevInbox.Dashboard.Internal;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(DevInboxChannelsView))]
[JsonSerializable(typeof(DevInboxList<DevInboxEmailRow>))]
[JsonSerializable(typeof(DevInboxList<DevInboxSmsRow>))]
[JsonSerializable(typeof(DevInboxList<DevInboxPushRow>))]
[JsonSerializable(typeof(DevInboxEmailRow))]
[JsonSerializable(typeof(DevInboxSmsRow))]
[JsonSerializable(typeof(DevInboxPushRow))]
[JsonSerializable(typeof(DevInboxEmailDetail))]
[JsonSerializable(typeof(DevInboxSmsDetail))]
[JsonSerializable(typeof(DevInboxPushDetail))]
internal sealed partial class DevInboxJsonContext : JsonSerializerContext;

/// <summary>One channel the host configured: its kind (<c>email</c>, <c>sms</c>, <c>push</c>) and store capacity.</summary>
internal sealed record DevInboxChannelView(string Kind, int Capacity);

/// <summary>The channels the host configured, so the page can name the provider call a missing one needs.</summary>
internal sealed record DevInboxChannelsView(IReadOnlyList<DevInboxChannelView> Channels);
