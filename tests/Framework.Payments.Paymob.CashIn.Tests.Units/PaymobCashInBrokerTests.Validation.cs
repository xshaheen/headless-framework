// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Callback;
using Microsoft.Extensions.Options;

namespace Tests;

public partial class PaymobCashInBrokerTests
{
    public static readonly TheoryData<string, string, string, bool> TransactionData =
        new()
        {
            {
                """
                    {
                      "id": 11113222,
                      "amount_cents": 50000,
                      "integration_id": 1111,
                      "currency": "EGP",
                      "created_at": "2021-08-02T15:45:26.988117",
                      "owner": 1188,
                      "error_occured": false,
                      "has_parent_transaction": false,
                      "redirect_url": null,
                      "order": {
                        "id": 11112345,
                        "amount_cents": 50000,
                        "paid_amount_cents": 50000,
                        "payment_method": "tbc"
                      },
                      "source_data": {
                        "pan": "1234",
                        "type": "card",
                        "sub_type": "MasterCard"
                      },
                      "data": {
                        "bill_reference": 0
                      },
                      "pending": false,
                      "success": true,
                      "is_auth": false,
                      "is_capture": false,
                      "is_voided": false,
                      "is_refunded": false,
                      "is_3d_secure": true,
                      "is_standalone_payment": true
                    }
                    """,
                "97A5B79200124C9E94E9D73763265D20",
                "62a79c8d174f39d114ded8ad4885af25889b53deeae91d2ff6ddb7202188043e4c492686ce0780c91afc3fd4beb7728d95d7afb64dafa0d522ccacb760cbb950",
                true
            },
            {
                """
                    {
                      "id": 11113222,
                      "amount_cents": 90000,
                      "integration_id": 1111,
                      "currency": "EGP",
                      "created_at": "2021-08-02T15:45:26.988117",
                      "owner": 1188,
                      "error_occured": false,
                      "has_parent_transaction": false,
                      "redirect_url": null,
                      "order": {
                        "id": 11112345,
                        "amount_cents": 50000,
                        "paid_amount_cents": 50000,
                        "payment_method": "tbc"
                      },
                      "source_data": {
                        "pan": "1234",
                        "type": "card",
                        "sub_type": "MasterCard"
                      },
                      "data": {
                        "bill_reference": 0
                      },
                      "pending": false,
                      "success": true,
                      "is_auth": false,
                      "is_capture": false,
                      "is_voided": false,
                      "is_refunded": false,
                      "is_3d_secure": true,
                      "is_standalone_payment": true
                    }

                    """,
                "97A5B79200124C9E94E9D73763265D20",
                "62a79c8d174f39d114ded8ad4885af25889b53deeae91d2ff6ddb7202188043e4c492686ce0780c91afc3fd4beb7728d95d7afb64dafa0d522ccacb760cbb950",
                false
            },
        };

    public static readonly TheoryData<string, string, string, bool> TokenData =
        new()
        {
            {
                """
                    {
                        "id": 111999,
                        "token": "12ast1f6305b97f7c40f3fffffb699f5452b50881b36b1",
                        "masked_pan": "xxxx-xxxx-xxxx-1234",
                        "merchant_id": 1112235,
                        "card_subtype": "Visa",
                        "created_at": "2021-08-04T22:57:48.155912",
                        "email": "mahmoud@xshaheen.com",
                        "order_id": "11111231",
                        "user_added": false
                    }
                    """,
                "97A5B79200124C9E94E9D73763265D20",
                "af87151d34e0db8855150ea0ce27895601316a72f4d8298d589944d74ccdc3158e93eb9892aaad9693961655793a6cd4498def045a2990f1caf9a39742a0d4c8",
                true
            },
        };

    [Theory]
    [MemberData(nameof(TransactionData))]
    public void should_validate_transaction_hmac_as_expected(
        string transactionJson,
        string validationKey,
        string expectedHmac,
        bool valid
    )
    {
        // given
        var options = Substitute.For<IOptionsMonitor<PaymobCashInOptions>>();
        options.CurrentValue.Returns(_ => new PaymobCashInOptions
        {
            Hmac = validationKey,
            ApiKey = Guid.NewGuid().ToString(),
        });
        var sut = new PaymobCashInBroker(null!, null!, options);
        var transaction = JsonSerializer.Deserialize<CashInCallbackTransaction>(transactionJson);

        // when
        var result = sut.Validate(transaction!.ToConcatenatedString(), expectedHmac);

        // then
        result.Should().Be(valid);
    }

    [Theory]
    [MemberData(nameof(TokenData))]
    public void should_validate_token_hmac_as_expected(
        string tokenJson,
        string validationKey,
        string expectedHmac,
        bool valid
    )
    {
        // given
        var options = Substitute.For<IOptionsMonitor<PaymobCashInOptions>>();
        options.CurrentValue.Returns(_ => new PaymobCashInOptions
        {
            Hmac = validationKey,
            ApiKey = Guid.NewGuid().ToString(),
        });
        var sut = new PaymobCashInBroker(null!, null!, options);
        var token = JsonSerializer.Deserialize<CashInCallbackToken>(tokenJson);

        // when
        var result = sut.Validate(token!.ToConcatenatedString(), expectedHmac);

        // then
        result.Should().Be(valid);
    }
}
