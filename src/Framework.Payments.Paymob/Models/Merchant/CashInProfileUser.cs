// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Merchant;

[PublicAPI]
public sealed class CashInProfileUser
{
    private readonly IReadOnlyList<object?>? _groups;
    private readonly IReadOnlyList<int>? _userPermissions;

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
    public IReadOnlyList<object?> Groups
    {
        get => _groups ?? Array.Empty<object?>();
        init => _groups = value;
    }

    [JsonPropertyName("user_permissions")]
    public IReadOnlyList<int> UserPermissions
    {
        get => _userPermissions ?? Array.Empty<int>();
        init => _userPermissions = value;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
