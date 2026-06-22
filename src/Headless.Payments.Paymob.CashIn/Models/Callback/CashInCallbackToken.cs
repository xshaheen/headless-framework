// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Internals;
using Humanizer;

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

/// <summary>
/// Represents a saved-card token issued by Paymob after a successful card payment when the
/// customer consents to saving their card for future use.
/// </summary>
/// <remarks>
/// Paymob delivers this object inside a <c>CashInCallback</c> envelope whose <c>Type</c> is
/// <c>CashInCallbackTypes.Token</c>. Verify authenticity with
/// <c>IPaymobCashInBroker.Validate(CashInCallbackToken, string)</c> before persisting the token.
/// </remarks>
[PublicAPI]
public sealed class CashInCallbackToken
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("masked_pan")]
    public required string MaskedPan { get; init; }

    [JsonPropertyName("merchant_id")]
    public int MerchantId { get; init; }

    [JsonPropertyName("card_subtype")]
    public required string CardSubtype { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("order_id")]
    public required string OrderId { get; init; }

    [JsonPropertyName("user_added")]
    public bool UserAdded { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }

    /// <summary>
    /// Produces the HMAC input string for this token by concatenating the required fields in
    /// Paymob's defined order.
    /// </summary>
    /// <remarks>Pass the result directly to <c>IPaymobCashInBroker.Validate(string, string)</c>.</remarks>
    public string ToConcatenatedString()
    {
        return CardSubtype
            + CreatedAt
            + Email
            + Id.ToString(CultureInfo.InvariantCulture)
            + MaskedPan
            + MerchantId.ToString(CultureInfo.InvariantCulture)
            + OrderId
            + Token;
    }

    /// <summary>
    /// Parses <c>CreatedAt</c> into a <see cref="DateTimeOffset"/>, applying the Egypt/Cairo UTC+2
    /// offset when the raw string carries no timezone information.
    /// </summary>
    public DateTimeOffset CreatedAtDateTimeOffset()
    {
        var dateTime = DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture);

        // If not have time zone offset, consider it cairo time.
        return dateTime.Kind is DateTimeKind.Unspecified
            ? new DateTimeOffset(
                dateTime,
                AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter.EgyptTimeZone.GetUtcOffset(dateTime)
            )
            : DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns normalised card information derived from the token's masked PAN and card subtype.
    /// </summary>
    public CashInCardInfo Card()
    {
        var type = CardSubtype?.ToUpperInvariant() switch
        {
            "MASTERCARD" => "MasterCard",
            "VISA" => "Visa",
            null => null,
            _ => CardSubtype.Humanize(),
        };

        return new(MaskedPan, type, Bank: null);
    }
}
