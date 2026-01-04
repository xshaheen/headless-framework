// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Emails;

[PublicAPI]
public sealed record SendSingleEmailRequest
{
    /// <summary>The email address to use as the "From" address for the email.</summary>
    public required EmailRequestAddress From { get; init; }

    /// <summary>An object that contains the recipients of the email message.</summary>
    public required EmailRequestDestination Destination { get; init; }

    /// <summary>The subject line of the email.</summary>
    public required string Subject { get; init; }

    /// <summary>The message to be sent in HTML.</summary>
    public string? MessageHtml { get; init; }

    /// <summary>The message to be sent in plain text.</summary>
    public string? MessageText { get; init; }

    /// <summary>The list of attachments to include in the email.</summary>
    public IReadOnlyList<EmailRequestAttachment> Attachments { get; init; } = [];
}

public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    public static EmailRequestAddress FromString(string operand) => operand;

    public override string ToString()
    {
        return DisplayName is null ? EmailAddress : $"{DisplayName} <{EmailAddress}>";
    }
}

public sealed class EmailRequestDestination
{
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = [];

    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = [];
}

public sealed class EmailRequestAttachment
{
    public string? Name { get; init; }

    public required byte[] File { get; init; }
}
