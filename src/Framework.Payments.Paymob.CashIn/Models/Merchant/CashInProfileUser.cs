// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Payments.Paymob.CashIn.Internals;

namespace Framework.Payments.Paymob.CashIn.Models.Merchant;

[PublicAPI]
public sealed class CashInProfileUser
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("date_joined")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset DateJoined { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    [JsonPropertyName("is_staff")]
    public bool IsStaff { get; init; }

    [JsonPropertyName("is_superuser")]
    public bool IsSuperuser { get; init; }

    [JsonPropertyName("last_login")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset? LastLogin { get; init; }

    [JsonPropertyName("groups")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> Groups
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("user_permissions")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<int> UserPermissions
    {
        get => field ?? [];
        init;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
