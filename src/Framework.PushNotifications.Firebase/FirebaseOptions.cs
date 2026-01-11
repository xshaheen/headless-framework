// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.PushNotifications.Firebase;

/// <summary>
/// Firebase configuration options.
/// </summary>
public sealed class FirebaseOptions
{
    /// <summary>
    /// Firebase service account JSON credentials.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize.
    /// </remarks>
    [JsonIgnore]
    public required string Json { get; init; }

    /// <inheritdoc />
    public override string ToString() => "FirebaseOptions { Json = [REDACTED] }";
}
