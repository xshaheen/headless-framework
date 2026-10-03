// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Cequens.Internal;

internal sealed class SigningInResponse
{
    [JsonPropertyName("data")]
    public SigningInDataResponse? Data { get; init; }
}

internal sealed class SigningInDataResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
}
