// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashOut.Models;

namespace Tests;

public sealed class CashOutTransactionTests
{
    [Theory]
    [InlineData("success", true)]
    [InlineData("successful", true)]
    [InlineData("failed", false)]
    [InlineData("pending", false)]
    [InlineData("SUCCESS", false)] // exact-match contract: Paymob sends lowercase statuses
    public void should_report_success_only_for_success_statuses(string status, bool expected)
    {
        var transaction = _Transaction(status, statusCode: "200");

        transaction.IsSuccess().Should().Be(expected);
    }

    [Theory]
    [InlineData("pending", "8000", true)]
    [InlineData("pending", "200", false)]
    [InlineData("failed", "8000", false)]
    public void should_report_pending_only_when_pending_status_with_8000_code(
        string status,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction(status, statusCode);

        transaction.IsPending().Should().Be(expected);
    }

    [Theory]
    [InlineData("failed", "401", true)]
    [InlineData("failed", "400", false)]
    [InlineData("success", "401", false)]
    public void should_report_authentication_error_only_when_failed_with_401(
        string status,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction(status, statusCode);

        transaction.IsAuthenticationError().Should().Be(expected);
    }

    [Theory]
    [InlineData("failed", "vodafone", "618", true)]
    [InlineData("failed", "etisalat", "618", false)]
    [InlineData("failed", "vodafone", "619", false)]
    [InlineData("success", "vodafone", "618", false)]
    public void should_report_vodafone_wallet_error_only_for_vodafone_618(
        string status,
        string issuer,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction(status, statusCode, issuer);

        transaction.IsNotHaveVodafoneCashError().Should().Be(expected);
    }

    [Theory]
    [InlineData("failed", "etisalat", "90040", true)]
    [InlineData("failed", "vodafone", "90040", false)]
    [InlineData("failed", "etisalat", "90041", false)]
    public void should_report_etisalat_wallet_error_only_for_etisalat_90040(
        string status,
        string issuer,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction(status, statusCode, issuer);

        transaction.IsNotHaveEtisalatCashError().Should().Be(expected);
    }

    [Theory]
    [InlineData("vodafone", "501", true)]
    [InlineData("vodafone", "6097", true)]
    [InlineData("etisalat", "90005", true)]
    [InlineData("etisalat", "90006", true)]
    [InlineData("vodafone", "90005", false)] // codes are issuer-specific
    [InlineData("etisalat", "501", false)]
    [InlineData("aman", "501", false)]
    public void should_report_provider_down_only_for_issuer_specific_codes(
        string issuer,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction("failed", statusCode, issuer);

        transaction.IsProviderDownError().Should().Be(expected);
    }

    [Fact]
    public void should_not_report_provider_down_when_not_failed()
    {
        var transaction = _Transaction("success", statusCode: "501", issuer: "vodafone");

        transaction.IsProviderDownError().Should().BeFalse();
    }

    [Theory]
    [InlineData("failed", "400", true)]
    [InlineData("failed", "401", false)]
    [InlineData("pending", "400", false)]
    public void should_report_request_validation_error_only_when_failed_with_400(
        string status,
        string statusCode,
        bool expected
    )
    {
        var transaction = _Transaction(status, statusCode);

        transaction.IsRequestValidationError().Should().Be(expected);
    }

    private static CashOutTransaction _Transaction(string status, string statusCode, string? issuer = null)
    {
        return new CashOutTransaction
        {
            DisbursementStatus = status,
            StatusCode = statusCode,
            Issuer = issuer,
        };
    }
}
