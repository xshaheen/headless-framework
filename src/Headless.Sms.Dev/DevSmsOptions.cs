// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Dev;

/// <summary>Configures a development SMS sender registered with <c>UseDevelopment</c>.</summary>
/// <remarks>
/// The properties use <see langword="set"/> so the <c>UseDevelopment(Action&lt;DevSmsOptions&gt;)</c> overloads can
/// change an instance the options system already constructed.
/// </remarks>
[PublicAPI]
public sealed class DevSmsOptions
{
    /// <summary>
    /// Gets or sets the file to append a plain-text copy of each message to, or <see langword="null"/> to keep
    /// messages in the capture store only. The file is created when missing; existing content is preserved.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets how many messages <see cref="ISmsCaptureStore"/> keeps before it drops the oldest. The default is
    /// 500.
    /// </summary>
    /// <remarks>
    /// All development SMS senders in a process share one store. When several set different values, the largest one
    /// applies.
    /// </remarks>
    public int CaptureCapacity { get; set; } = 500;
}

internal sealed class DevSmsOptionsValidator : AbstractValidator<DevSmsOptions>
{
    public DevSmsOptionsValidator()
    {
        RuleFor(x => x.FilePath).NotEmpty().When(x => x.FilePath is not null);
        RuleFor(x => x.CaptureCapacity).GreaterThan(0);
    }
}
