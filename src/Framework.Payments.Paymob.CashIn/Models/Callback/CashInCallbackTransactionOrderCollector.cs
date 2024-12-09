// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackTransactionOrderCollector
{
    private readonly IReadOnlyList<object?>? _companyEmails;
    private readonly IReadOnlyList<object?>? _phones;

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("company_name")]
    public required string CompanyName { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("street")]
    public string? Street { get; init; }

    [JsonPropertyName("phones")]
    public IReadOnlyList<object?> Phones
    {
        get => _phones ?? Array.Empty<object?>();
        init => _phones = value;
    }

    [JsonPropertyName("company_emails")]
    public IReadOnlyList<object?> CompanyEmails
    {
        get => _companyEmails ?? Array.Empty<object?>();
        init => _companyEmails = value;
    }
}
