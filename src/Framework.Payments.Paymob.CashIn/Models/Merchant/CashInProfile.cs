// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Payments.Paymob.CashIn.Internals;

namespace Framework.Payments.Paymob.CashIn.Models.Merchant;

[PublicAPI]
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class CashInProfile
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("user")]
    public required CashInProfileUser User { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("profile_type")]
    public required string ProfileType { get; init; }

    [JsonPropertyName("phones")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<string> Phones
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("company_emails")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<string> CompanyEmails
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("company_name")]
    public required string CompanyName { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [JsonPropertyName("city")]
    public required string City { get; init; }

    [JsonPropertyName("postal_code")]
    public required string PostalCode { get; init; }

    [JsonPropertyName("street")]
    public required string Street { get; init; }

    [JsonPropertyName("email_notification")]
    public bool EmailNotification { get; init; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("is_mobadra")]
    public bool IsMobadra { get; init; }

    [JsonPropertyName("sector")]
    public string? Sector { get; init; }

    [JsonPropertyName("failed_attempts")]
    public int FailedAttempts { get; init; }

    [JsonPropertyName("profile_phash")]
    public string? ProfilePhash { get; init; }

    [JsonPropertyName("is_temp_password")]
    public bool IsTempPassword { get; init; }

    [JsonPropertyName("delivery_status_callback")]
    public string? DeliveryStatusCallback { get; init; }

    [JsonPropertyName("merchant_status")]
    public int MerchantStatus { get; init; }

    [JsonPropertyName("deactivated_by_bank")]
    public bool DeactivatedByBank { get; init; }

    [JsonPropertyName("bank_merchant_status")]
    public int BankMerchantStatus { get; init; }

    [JsonPropertyName("allow_terminal_order_id")]
    public bool AllowTerminalOrderId { get; init; }

    [JsonPropertyName("allow_encryption_bypass")]
    public bool AllowEncryptionBypass { get; init; }

    [JsonPropertyName("suspicious")]
    public int Suspicious { get; init; }

    [JsonPropertyName("bank_received_documents")]
    public bool BankReceivedDocuments { get; init; }

    [JsonPropertyName("bank_merchant_digital_status")]
    public int BankMerchantDigitalStatus { get; init; }

    [JsonPropertyName("filled_business_data")]
    public bool FilledBusinessData { get; init; }

    [JsonPropertyName("withhold_transfers")]
    public bool WithholdTransfers { get; init; }

    [JsonPropertyName("day_start_time")]
    public string? DayStartTime { get; init; }

    [JsonPropertyName("day_end_time")]
    public string? DayEndTime { get; init; }

    [JsonPropertyName("sms_sender_name")]
    public string? SmsSenderName { get; init; }

    [JsonPropertyName("can_bill_deposit_with_card")]
    public bool CanBillDepositWithCard { get; init; }

    [JsonPropertyName("can_topup_merchants")]
    public bool CanTopupMerchants { get; init; }

    [JsonPropertyName("sales_owner")]
    public string? SalesOwner { get; init; }

    [JsonPropertyName("password")]
    public object? Password { get; init; }

    [JsonPropertyName("username")]
    public object? Username { get; init; }

    [JsonPropertyName("merchant_external_link")]
    public object? MerchantExternalLink { get; init; }

    [JsonPropertyName("order_retrieval_endpoint")]
    public object? OrderRetrievalEndpoint { get; init; }

    [JsonPropertyName("delivery_update_endpoint")]
    public object? DeliveryUpdateEndpoint { get; init; }

    [JsonPropertyName("awb_banner")]
    public object? AwbBanner { get; init; }

    [JsonPropertyName("email_banner")]
    public object? EmailBanner { get; init; }

    [JsonPropertyName("identification_number")]
    public object? IdentificationNumber { get; init; }

    [JsonPropertyName("bank_deactivation_reason")]
    public object? BankDeactivationReason { get; init; }

    [JsonPropertyName("national_id")]
    public object? NationalId { get; init; }

    [JsonPropertyName("super_agent")]
    public object? SuperAgent { get; init; }

    [JsonPropertyName("wallet_limit_profile")]
    public object? WalletLimitProfile { get; init; }

    [JsonPropertyName("address")]
    public object? Address { get; init; }

    [JsonPropertyName("commercial_registration")]
    public object? CommercialRegistration { get; init; }

    [JsonPropertyName("commercial_registration_area")]
    public object? CommercialRegistrationArea { get; init; }

    [JsonPropertyName("distributor_code")]
    public object? DistributorCode { get; init; }

    [JsonPropertyName("distributor_branch_code")]
    public object? DistributorBranchCode { get; init; }

    [JsonPropertyName("wallet_phone_number")]
    public object? WalletPhoneNumber { get; init; }

    [JsonPropertyName("latitude")]
    public object? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public object? Longitude { get; init; }

    [JsonPropertyName("bank_staffs")]
    public object? BankStaffs { get; init; }

    [JsonPropertyName("bank_rejection_reason")]
    public object? BankRejectionReason { get; init; }

    [JsonPropertyName("bank_digital_rejection_reason")]
    public object? BankDigitalRejectionReason { get; init; }

    [JsonPropertyName("random_secret")]
    public object? RandomSecret { get; init; }

    [JsonPropertyName("random_iv")]
    public object? RandomIv { get; init; }

    [JsonPropertyName("withhold_transfers_reason")]
    public object? WithholdTransfersReason { get; init; }

    [JsonPropertyName("withhold_transfers_notes")]
    public object? WithholdTransfersNotes { get; init; }

    [JsonPropertyName("topup_transfer_id")]
    public object? TopupTransferId { get; init; }

    [JsonPropertyName("acq_partner")]
    public object? AcqPartner { get; init; }

    [JsonPropertyName("dom")]
    public object? Dom { get; init; }

    [JsonPropertyName("bank_related")]
    public object? BankRelated { get; init; }

    [JsonPropertyName("custom_export_columns")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> CustomExportColumns
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("server_IP")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> ServerIp
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("permissions")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<object?> Permissions
    {
        get => field ?? [];
        init;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
