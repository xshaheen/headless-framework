// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.PushNotifications.Dev;

/// <summary>Configures a development push notification service registered with <c>UseDevelopment</c>.</summary>
/// <remarks>
/// The properties use <see langword="set"/> so the <c>UseDevelopment(Action&lt;DevPushNotificationOptions&gt;)</c>
/// overloads can change an instance the options system already constructed.
/// </remarks>
[PublicAPI]
public sealed class DevPushNotificationOptions
{
    /// <summary>
    /// Gets or sets how many notifications <see cref="IPushNotificationCaptureStore"/> keeps before it drops the
    /// oldest. The default is 500.
    /// </summary>
    /// <remarks>
    /// All development push notification services in a process share one store. When several set different values,
    /// the largest one applies.
    /// </remarks>
    public int CaptureCapacity { get; set; } = 500;
}

internal sealed class DevPushNotificationOptionsValidator : AbstractValidator<DevPushNotificationOptions>
{
    public DevPushNotificationOptionsValidator()
    {
        RuleFor(x => x.CaptureCapacity).GreaterThan(0);
    }
}
