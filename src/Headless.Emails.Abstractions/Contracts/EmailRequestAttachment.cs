// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// A binary file attachment to include in an outgoing email.
/// </summary>
[PublicAPI]
public sealed record EmailRequestAttachment
{
    /// <summary>The file name shown to the recipient (e.g. <c>invoice.pdf</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The raw file bytes to attach.</summary>
    public required ReadOnlyMemory<byte> File { get; init; }

    /// <summary>
    /// The MIME content type (e.g. <c>application/pdf</c>). When <see langword="null"/>,
    /// the type is inferred from the <see cref="Name"/> extension.
    /// </summary>
    public string? ContentType { get; init; }
}
