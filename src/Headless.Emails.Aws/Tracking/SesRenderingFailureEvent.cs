// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>Rendering Failure</c> event.</summary>
[PublicAPI]
public sealed record SesRenderingFailureEvent
{
    /// <summary>The name of the template used to send the email.</summary>
    [JsonPropertyName("templateName")]
    public string TemplateName { get; init; } = null!;

    /// <summary>A message that provides more information about the rendering failure.</summary>
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = null!;
}
