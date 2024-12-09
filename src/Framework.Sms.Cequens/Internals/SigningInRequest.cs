// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Cequens.Internals;

public sealed class SigningInRequest(string apiKey, string userName)
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = apiKey;

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = userName;
}
