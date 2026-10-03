// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Cequens.Internal;

internal sealed class SigningInDataResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
}
