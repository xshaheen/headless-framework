// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Emails.Contracts;

[PublicAPI]
public sealed record SendSingleEmailRequest
{
    public required EmailRequestAddress From { get; init; }

    public required EmailRequestDestination Destination { get; init; }

    public required string Subject { get; init; }

    public string? MessageHtml { get; init; }

    public string? MessageText { get; init; }

    public IReadOnlyList<EmailRequestAttachment> Attachments { get; init; } = [];
}

public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    public static EmailRequestAddress FromString(string operand) => operand;
}

public sealed class EmailRequestDestination
{
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = Array.Empty<EmailRequestAddress>();

    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = Array.Empty<EmailRequestAddress>();
}

public sealed class EmailRequestAttachment
{
    public string? Name { get; init; }

    public required byte[] File { get; init; }
}
