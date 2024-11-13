// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.Authentication.ApiKey;

public interface IApiKeyStore<TUser, TUserId>
    where TUser : IdentityUser<TUserId>
    where TUserId : IEquatable<TUserId>
{
    /// <summary>Get api key user if the key exists and active.</summary>
    /// <returns>Return null if the api key doesn't exist or not active otherwise return the user associated with the api key.</returns>
    ValueTask<TUser?> GetActiveApiKeyUserAsync(string apiKey);
}
