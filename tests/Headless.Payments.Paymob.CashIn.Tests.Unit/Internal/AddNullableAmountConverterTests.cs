// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Internal;

namespace Tests.Internal;

public sealed class AddNullableAmountConverterTests
{
    private const string _SavedTokenPayWithBooleanFlags = """
        {
          "id": 1,
          "success": false,
          "pending": false,
          "is_auth": false,
          "is_capture": false,
          "is_standalone_payment": true,
          "is_voided": false,
          "is_refunded": false,
          "is_3d_secure": true,
          "has_parent_transaction": false,
          "currency": "EGP",
          "is_void": false,
          "is_refund": false,
          "error_occured": true,
          "hmac": "h"
        }
        """;

    [Theory]
    [InlineData("\"N/A\"", "N/A")]
    [InlineData("\"12.5\"", "12.5")]
    [InlineData("12.5", "12.5")]
    public void should_read_string_and_number_tokens_when_reading_an_amount(string token, string expected)
    {
        var transaction = JsonSerializer.Deserialize<CashInTransaction>(
            _Transaction(token),
            CashInJsonOptions.JsonOptions
        );

        transaction!.Fees.Should().Be(expected);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void should_throw_json_exception_when_an_amount_is_not_a_number_or_string(string token)
    {
        var act = () =>
            JsonSerializer.Deserialize<CashInTransaction>(_Transaction(token), CashInJsonOptions.JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_throw_json_exception_when_a_string_flag_receives_a_json_boolean()
    {
        // Both option sets over the CashIn context must fail loudly: a declined charge whose
        // error_occured flag read as a placeholder would look like a pending one.
        var runtime = () =>
            JsonSerializer.Deserialize<CashInSavedTokenPayResponse>(
                _SavedTokenPayWithBooleanFlags,
                CashInJsonOptions.JsonOptions
            );
        var contextDefault = () =>
            JsonSerializer.Deserialize<CashInSavedTokenPayResponse>(
                _SavedTokenPayWithBooleanFlags,
                PaymobCashInJsonSerializerContext.Default.Options
            );

        runtime.Should().Throw<JsonException>();
        contextDefault.Should().Throw<JsonException>();
    }

    private static string _Transaction(string feesToken)
    {
        return $$"""{ "id": 1, "currency": "EGP", "api_source": "INVOICE", "fees": {{feesToken}} }""";
    }
}
