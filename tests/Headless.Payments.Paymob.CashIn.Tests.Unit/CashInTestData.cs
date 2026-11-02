// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;

namespace Tests;

/// <summary>
/// Builds Paymob Cash In request and response graphs with every property set to a non-default value,
/// so a test that round-trips a response through WireMock and compares the JSON checks every field.
/// Untyped (<see cref="object"/>) properties stay <see langword="null"/> because their wire shape is
/// unknown and any other value would not survive the round trip.
/// </summary>
internal static class CashInTestData
{
    /// <summary>
    /// Creates a <see cref="Faker"/> seeded from the running test's display name, so each test and
    /// theory row draws a stable sequence: a failure reproduces on re-run, and tests that share the
    /// class fixture's WireMock server do not generate the same request bodies.
    /// </summary>
    public static Faker CreateFaker()
    {
        var testName = TestContext.Current.Test?.TestDisplayName ?? nameof(CashInTestData);

        return new Faker { Random = new Randomizer(localSeed: _StableHash(testName)) };
    }

    public static string Text(Faker f) => f.Random.AlphaNumeric(24);

    public static CashInAuthenticationTokenResponse AuthenticationTokenResponse(Faker f)
    {
        return new()
        {
            Token = Text(f),
            Profile = _Profile(f),
            ExtensionData = _ExtensionData(f),
        };
    }

    public static CashInPaymentKeyResponse PaymentKeyResponse(Faker f)
    {
        return new() { PaymentKey = Text(f) };
    }

    public static CashInBillingData BillingData(Faker f)
    {
        return new(
            firstName: f.Name.FirstName(),
            lastName: f.Name.LastName(),
            phoneNumber: f.Phone.PhoneNumber(),
            email: f.Internet.Email()
        )
        {
            Country = f.Address.Country(),
            State = f.Address.State(),
            City = f.Address.City(),
            Apartment = f.Random.Int(1, 99).ToString(CultureInfo.InvariantCulture),
            Street = f.Address.StreetName(),
            Floor = f.Random.Int(1, 30).ToString(CultureInfo.InvariantCulture),
            Building = f.Address.BuildingNumber(),
            ShippingMethod = Text(f),
            PostalCode = f.Address.ZipCode(),
        };
    }

    public static CashInPaymentKeyRequest PaymentKeyRequest(Faker f, int? expiration)
    {
        return new(
            integrationId: _Id(f),
            orderId: _Id(f),
            billingData: BillingData(f),
            amountCents: _Amount(f),
            currency: f.Finance.Currency().Code,
            lockOrderWhenPaid: f.Random.Bool(),
            expiration: expiration
        );
    }

    public static CashInCreateOrderResponse CreateOrderResponse(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            MerchantOrderId = Text(f),
            CreatedAt = _Date(f),
            AmountCents = _Amount(f),
            PaidAmountCents = _Amount(f),
            Currency = f.Finance.Currency().Code,
            CommissionFees = _Amount(f),
            DeliveryFeesCents = _Amount(f),
            DeliveryVatCents = _Amount(f),
            IsPaymentLocked = true,
            IsReturn = true,
            IsCancel = true,
            IsReturned = true,
            IsCanceled = true,
            DeliveryNeeded = true,
            NotifyUserWithEmail = true,
            PaymentMethod = Text(f),
            ApiSource = Text(f),
            Token = Text(f),
            OrderUrl = f.Internet.Url(),
            Url = f.Internet.Url(),
            Merchant = _OrderMerchant(f),
            ShippingData = _OrderShippingData(f),
            ShippingDetails = _OrderShippingDetails(f),
            Items = [_OrderItem(f), _OrderItem(f)],
            DeliveryStatus = _NullItems(),
            ExtensionData = _ExtensionData(f),
        };
    }

    public static CashInWalletPayResponse WalletPayResponse(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            Pending = true,
            AmountCents = _Amount(f),
            Success = true,
            IsAuth = true,
            IsCapture = true,
            IsStandalonePayment = true,
            IsVoided = true,
            IsRefunded = true,
            Is3DSecure = true,
            IntegrationId = _Id(f),
            ProfileId = _Id(f),
            HasParentTransaction = true,
            CreatedAt = _Date(f),
            Currency = f.Finance.Currency().Code,
            ApiSource = Text(f),
            MerchantCommission = _Amount(f),
            IsVoid = true,
            IsRefund = true,
            IsHidden = true,
            IsLive = true,
            RefundedAmountCents = _Amount(f),
            SourceId = _Id(f),
            IsCaptured = true,
            CapturedAmount = _Amount(f),
            Owner = _Id(f),
            ErrorOccured = true,
            OtherEndpointReference = Text(f),
            TerminalId = Text(f),
            RedirectUrl = f.Internet.Url(),
            IframeRedirectionUrl = f.Internet.Url(),
            Data = _WalletData(f),
            PaySourceData = new CashInWalletPaySourceData
            {
                OwnerName = f.Name.FullName(),
                SubType = Text(f),
                Pan = Text(f),
                PhoneNumber = f.Phone.PhoneNumber(),
                Type = Text(f),
                ExtensionData = _ExtensionData(f),
            },
            Order = _Order(f),
            PaymentKeyClaims = _PaymentKeyClaims(f),
            TransactionProcessedCallbackResponses = _NullItems(),
            ExtensionData = _ExtensionData(f),
        };
    }

    public static CashInKioskPayResponse KioskPayResponse(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            Pending = true,
            AmountCents = _Amount(f),
            Success = true,
            IsAuth = true,
            IsCapture = true,
            IsStandalonePayment = true,
            IsVoided = true,
            IsRefunded = true,
            Is3DSecure = true,
            IntegrationId = _Id(f),
            ProfileId = _Id(f),
            HasParentTransaction = true,
            CreatedAt = _Date(f),
            Currency = f.Finance.Currency().Code,
            ApiSource = Text(f),
            MerchantCommission = _Amount(f),
            IsVoid = true,
            IsRefund = true,
            IsHidden = true,
            ErrorOccured = true,
            IsLive = true,
            RefundedAmountCents = _Amount(f),
            Owner = _Id(f),
            SourceId = _Id(f),
            IsCaptured = true,
            CapturedAmount = _Amount(f),
            TerminalId = Text(f),
            Order = _Order(f),
            SourceData = new CashInKioskPaySourceData
            {
                SubType = Text(f),
                Pan = Text(f),
                Type = Text(f),
                ExtensionData = _ExtensionData(f),
            },
            Data = new CashInKioskPayData
            {
                GatewayIntegrationPk = _Id(f),
                Otp = Text(f),
                Klass = Text(f),
                TxnResponseCode = Text(f),
                BillReference = _Id(f),
                Message = Text(f),
                PaidThrough = Text(f),
                DueAmount = _Amount(f),
                ExtensionData = _ExtensionData(f),
            },
            PaymentKeyClaims = _PaymentKeyClaims(f),
            TransactionProcessedCallbackResponses = _NullItems(),
            ExtensionData = _ExtensionData(f),
        };
    }

    public static CashInSavedTokenPayResponse SavedTokenPayResponse(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            RedirectionUrl = f.Internet.Url(),
            Success = Text(f),
            Pending = Text(f),
            AmountCents = _Amount(f),
            IsAuth = Text(f),
            IsCapture = Text(f),
            IsStandalonePayment = Text(f),
            IsVoided = Text(f),
            IsRefunded = Text(f),
            Is3DSecure = Text(f),
            IntegrationId = _Id(f),
            ProfileId = _Id(f),
            HasParentTransaction = Text(f),
            Order = _Id(f),
            CreatedAt = _Date(f),
            Currency = f.Finance.Currency().Code,
            TerminalId = Text(f),
            MerchantCommission = _Amount(f),
            IsVoid = Text(f),
            IsRefund = Text(f),
            ErrorOccured = Text(f),
            RefundedAmountCents = _Amount(f),
            CapturedAmount = _Amount(f),
            MerchantStaffTag = Text(f),
            Owner = _Id(f),
            ParentTransaction = Text(f),
            MerchantOrderId = Text(f),
            DataMessage = Text(f),
            SourceDataType = Text(f),
            SourceDataPan = Text(f),
            SourceDataSubType = Text(f),
            AcqResponseCode = Text(f),
            TxnResponseCode = Text(f),
            Hmac = Text(f),
            UseRedirection = true,
            MerchantResponse = Text(f),
            BypassStepSix = true,
        };
    }

    public static CashInCashCollectionPayResponse CashCollectionPayResponse(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            Pending = Text(f),
            AmountCents = _Amount(f),
            Success = Text(f),
            IsAuth = Text(f),
            IsCapture = Text(f),
            IsStandalonePayment = Text(f),
            IsVoided = Text(f),
            IsRefunded = Text(f),
            Is3DSecure = Text(f),
            IntegrationId = _Id(f),
            ProfileId = _Id(f),
            HasParentTransaction = Text(f),
            Order = _Id(f),
            CreatedAt = _Date(f),
            Currency = f.Finance.Currency().Code,
            TerminalId = Text(f),
            MerchantCommission = _Amount(f),
            IsVoid = Text(f),
            IsRefund = Text(f),
            ErrorOccured = Text(f),
            RefundedAmountCents = _Amount(f),
            CapturedAmount = _Amount(f),
            MerchantStaffTag = Text(f),
            Owner = _Id(f),
            ParentTransaction = Text(f),
            MerchantOrderId = Text(f),
            DataMessage = Text(f),
            SourceDataType = Text(f),
            SourceDataPan = Text(f),
            SourceDataSubType = Text(f),
            AcqResponseCode = Text(f),
            TxnResponseCode = Text(f),
            Hmac = Text(f),
            UseRedirection = true,
            RedirectionUrl = f.Internet.Url(),
            MerchantResponse = Text(f),
            BypassStepSix = true,
        };
    }

    public static CashInCreateIntentionResponse CreateIntentionResponse(Faker f)
    {
        return new()
        {
            Id = Text(f),
            RedirectionUrl = f.Internet.Url(),
            IntentionOrderId = _Id(f),
            ClientSecret = Text(f),
            SpecialReference = Text(f),
            Confirmed = true,
            Status = Text(f),
            Created = f.Date.Recent(),
            CardTokens = [Text(f), Text(f)],
            Object = Text(f),
            IntentionDetail = new CashInCreateIntentionResponseIntentionDetail
            {
                Amount = _Amount(f),
                Currency = f.Finance.Currency().Code,
                Items =
                [
                    new CashInCreateIntentionResponseItem
                    {
                        Name = f.Commerce.ProductName(),
                        Amount = _Amount(f),
                        Description = f.Commerce.ProductDescription(),
                        Quantity = f.Random.Int(1, 10),
                    },
                ],
                BillingData = new CashInCreateIntentionResponseBillingData
                {
                    FirstName = f.Name.FirstName(),
                    LastName = f.Name.LastName(),
                    PhoneNumber = f.Phone.PhoneNumber(),
                    Email = f.Internet.Email(),
                    Country = f.Address.Country(),
                    Apartment = Text(f),
                    Floor = Text(f),
                    Street = f.Address.StreetName(),
                    City = f.Address.City(),
                    Building = f.Address.BuildingNumber(),
                    State = f.Address.State(),
                    PostalCode = f.Address.ZipCode(),
                    ShippingMethod = Text(f),
                },
            },
            PaymentKeys =
            [
                new CashInCreateIntentionResponsePaymentKey
                {
                    Integration = _Id(f),
                    Key = Text(f),
                    GatewayType = Text(f),
                    IframeId = Text(f),
                    OrderId = _Id(f),
                    RedirectionUrl = f.Internet.Url(),
                },
            ],
            PaymentMethods =
            [
                new CashInCreateIntentionResponsePaymentMethod
                {
                    IntegrationId = _Id(f),
                    MethodType = Text(f),
                    Currency = f.Finance.Currency().Code,
                    Live = true,
                    UseCvcWithMoto = true,
                },
            ],
            Extras = new CashInCreateIntentionResponseExtras
            {
                CreationExtras = new CashInCreateIntentionResponseCreationExtras
                {
                    MerchantOrderId = Text(f),
                    ExtensionData = _ExtensionData(f),
                },
                ConfirmationExtras = null,
            },
        };
    }

    private static CashInProfile _Profile(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            User = new CashInProfileUser
            {
                Id = _Id(f),
                Username = f.Internet.UserName(),
                FirstName = f.Name.FirstName(),
                LastName = f.Name.LastName(),
                DateJoined = _Date(f),
                Email = f.Internet.Email(),
                IsActive = true,
                IsStaff = true,
                IsSuperuser = true,
                LastLogin = _Date(f),
                Groups = _NullItems(),
                UserPermissions = [f.Random.Int(1, 1000), f.Random.Int(1, 1000)],
                ExtensionData = _ExtensionData(f),
            },
            CreatedAt = _Date(f),
            Active = true,
            ProfileType = Text(f),
            Phones = [f.Phone.PhoneNumber(), f.Phone.PhoneNumber()],
            CompanyEmails = [f.Internet.Email(), f.Internet.Email()],
            CompanyName = f.Company.CompanyName(),
            State = f.Address.State(),
            Country = f.Address.Country(),
            City = f.Address.City(),
            PostalCode = f.Address.ZipCode(),
            Street = f.Address.StreetName(),
            EmailNotification = true,
            LogoUrl = f.Internet.Url(),
            IsMobadra = true,
            Sector = Text(f),
            FailedAttempts = f.Random.Int(1, 10),
            ProfilePhash = Text(f),
            IsTempPassword = true,
            DeliveryStatusCallback = f.Internet.Url(),
            MerchantStatus = f.Random.Int(1, 10),
            DeactivatedByBank = true,
            BankMerchantStatus = f.Random.Int(1, 10),
            AllowTerminalOrderId = true,
            AllowEncryptionBypass = true,
            Suspicious = f.Random.Int(1, 10),
            BankReceivedDocuments = true,
            BankMerchantDigitalStatus = f.Random.Int(1, 10),
            FilledBusinessData = true,
            WithholdTransfers = true,
            DayStartTime = Text(f),
            DayEndTime = Text(f),
            SmsSenderName = Text(f),
            CanBillDepositWithCard = true,
            CanTopupMerchants = true,
            SalesOwner = Text(f),
            CustomExportColumns = _NullItems(),
            ServerIp = _NullItems(),
            Permissions = _NullItems(),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInWalletData _WalletData(Faker f)
    {
        return new()
        {
            CreatedAt = _Date(f),
            GatewayIntegrationPk = _Id(f),
            RedirectUrl = f.Internet.Url(),
            Klass = Text(f),
            MpgTxnId = Text(f),
            TxnResponseCode = Text(f),
            UigTxnId = Text(f),
            UpgTxnId = Text(f),
            Token = Text(f),
            OrderInfo = Text(f),
            Method = f.Random.Int(1, 10),
            WalletIssuer = Text(f),
            Message = Text(f),
            Amount = _Amount(f),
            Currency = f.Finance.Currency().Code,
            MerTxnRef = Text(f),
            UpgQrcodeRef = Text(f),
            WalletMsisdn = f.Phone.PhoneNumber(),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInOrder _Order(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            MerchantOrderId = Text(f),
            CreatedAt = _Date(f),
            DeliveryNeeded = true,
            AmountCents = _Amount(f),
            PaidAmountCents = _Amount(f),
            Currency = f.Finance.Currency().Code,
            IsPaymentLocked = true,
            IsReturn = true,
            IsCancel = true,
            IsReturned = true,
            IsCanceled = true,
            OrderUrl = f.Internet.Url(),
            CommissionFees = _Amount(f),
            DeliveryFeesCents = _Amount(f),
            DeliveryVatCents = _Amount(f),
            PaymentMethod = Text(f),
            ApiSource = Text(f),
            NotifyUserWithEmail = true,
            Merchant = _OrderMerchant(f),
            ShippingData = _OrderShippingData(f),
            ShippingDetails = _OrderShippingDetails(f),
            Items = [_OrderItem(f), _OrderItem(f)],
            DeliveryStatus = _NullItems(),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInOrderMerchant _OrderMerchant(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            CreatedAt = _Date(f),
            CompanyName = f.Company.CompanyName(),
            State = f.Address.State(),
            Country = f.Address.Country(),
            City = f.Address.City(),
            PostalCode = f.Address.ZipCode(),
            Street = f.Address.StreetName(),
            Phones = [f.Phone.PhoneNumber(), f.Phone.PhoneNumber()],
            CompanyEmails = [f.Internet.Email(), f.Internet.Email()],
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInOrderShippingData _OrderShippingData(Faker f)
    {
        return new()
        {
            Id = _Id(f),
            FirstName = f.Name.FirstName(),
            LastName = f.Name.LastName(),
            Email = f.Internet.Email(),
            PhoneNumber = f.Phone.PhoneNumber(),
            Street = f.Address.StreetName(),
            Building = f.Address.BuildingNumber(),
            Floor = Text(f),
            Apartment = Text(f),
            City = f.Address.City(),
            State = f.Address.State(),
            Country = f.Address.Country(),
            PostalCode = f.Address.ZipCode(),
            OrderId = _Id(f),
            Order = _Id(f),
            ExtraDescription = Text(f),
            ShippingMethod = Text(f),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInOrderShippingDetails _OrderShippingDetails(Faker f)
    {
        return new()
        {
            Notes = Text(f),
            NumberOfPackages = f.Random.Int(1, 10),
            Weight = f.Random.Int(1, 100),
            Length = f.Random.Int(1, 100),
            Width = f.Random.Int(1, 100),
            Height = f.Random.Int(1, 100),
            WeightUnit = Text(f),
            Contents = Text(f),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInOrderItem _OrderItem(Faker f)
    {
        return new()
        {
            Name = f.Commerce.ProductName(),
            Description = f.Commerce.ProductDescription(),
            AmountCents = _Amount(f),
            Quantity = f.Random.Int(1, 10),
            ExtensionData = _ExtensionData(f),
        };
    }

    private static CashInPayPaymentKeyClaims _PaymentKeyClaims(Faker f)
    {
        return new()
        {
            IntegrationId = _Id(f),
            AmountCents = _Amount(f),
            UserId = _Id(f),
            Currency = f.Finance.Currency().Code,
            Exp = f.Random.Int(1, 100_000),
            OrderId = _Id(f),
            PmkIp = f.Internet.Ip(),
            LockOrderWhenPaid = true,
            BillingData = new CashInPayPaymentKeyClaimsBillingData
            {
                FirstName = f.Name.FirstName(),
                LastName = f.Name.LastName(),
                Email = f.Internet.Email(),
                PhoneNumber = f.Phone.PhoneNumber(),
                Country = f.Address.Country(),
                State = f.Address.State(),
                City = f.Address.City(),
                Street = f.Address.StreetName(),
                Building = f.Address.BuildingNumber(),
                Floor = Text(f),
                Apartment = Text(f),
                PostalCode = f.Address.ZipCode(),
                ExtraDescription = Text(f),
            },
            ExtensionData = _ExtensionData(f),
        };
    }

    private static long _Id(Faker f) => f.Random.Long(1, 1_000_000_000);

    private static long _Amount(Faker f) => f.Random.Long(100, 10_000_000);

    private static DateTimeOffset _Date(Faker f) => f.Date.RecentOffset(days: 30);

    // Paymob sends untyped JSON arrays here; null items are the only element shape that round-trips.
    private static IReadOnlyList<object?> _NullItems() => [null, null];

    // Values are null for the same reason as the untyped properties: JSON null is the one value that
    // deserializes back to the same CLR value. Prefixed keys never collide with a mapped property.
    private static Dictionary<string, object?> _ExtensionData(Faker f)
    {
        return new(StringComparer.Ordinal)
        {
            ["extra_" + f.Random.AlphaNumeric(8)] = null,
            ["extra_" + f.Random.AlphaNumeric(8)] = null,
        };
    }

    // FNV-1a: string.GetHashCode is randomized per process, so it cannot give a seed that holds across runs.
    private static int _StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;

            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }
}
