// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Emails.Dev;

/// <summary>Configures a development email sender registered with <c>UseDevelopment</c>.</summary>
/// <remarks>
/// The properties use <see langword="set"/> so the <c>UseDevelopment(Action&lt;DevEmailOptions&gt;)</c> overloads can
/// change an instance the options system already constructed.
/// </remarks>
[PublicAPI]
public sealed class DevEmailOptions
{
    /// <summary>
    /// Gets or sets the file to append a plain-text copy of each email to, or <see langword="null"/> to keep emails in
    /// the capture store only. The file is created when missing; existing content is preserved.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets how many emails <see cref="IEmailCaptureStore"/> keeps before it drops the oldest. The default is
    /// 500.
    /// </summary>
    /// <remarks>
    /// All development email senders in a process share one store. When several set different values, the largest
    /// one applies.
    /// </remarks>
    public int CaptureCapacity { get; set; } = 500;

    /// <summary>
    /// Gets or sets the largest attachment, in bytes, whose content the store keeps. The default is 0, which keeps
    /// only each attachment's name, content type, and size.
    /// </summary>
    /// <remarks>
    /// Kept content makes the <c>.eml</c> export carry the attachment. The limit applies to each attachment, and every
    /// kept attachment stays in memory until the store drops or deletes its email. With a limit of L bytes and at most K
    /// attachments per email, kept content can reach <see cref="CaptureCapacity"/> × K × L bytes.
    /// </remarks>
    public int MaxCapturedAttachmentBytes { get; set; }
}

internal sealed class DevEmailOptionsValidator : AbstractValidator<DevEmailOptions>
{
    public DevEmailOptionsValidator()
    {
        RuleFor(x => x.FilePath).NotEmpty().When(x => x.FilePath is not null);
        RuleFor(x => x.CaptureCapacity).GreaterThan(0);
        RuleFor(x => x.MaxCapturedAttachmentBytes).GreaterThanOrEqualTo(0);
    }
}
