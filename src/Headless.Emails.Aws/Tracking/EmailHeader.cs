// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>A single email header, as reported in <see cref="MailDetails.Headers"/>.</summary>
/// <param name="Name">The header name.</param>
/// <param name="Value">The header value.</param>
[PublicAPI]
public sealed record EmailHeader(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value
);
