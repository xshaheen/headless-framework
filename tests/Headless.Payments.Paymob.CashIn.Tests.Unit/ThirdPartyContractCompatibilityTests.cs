// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Merchant;

namespace Tests;

public sealed class ThirdPartyContractCompatibilityTests
{
    [Fact]
    public void should_preserve_paymob_profile_user_date_joined_contract()
    {
        var joined = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(2));
        var user = new CashInProfileUser
        {
            Id = 1,
            Username = "provider-user",
            FirstName = "Provider",
            LastName = "User",
            DateJoined = joined,
            Email = "provider@example.com",
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(user));

        user.DateJoined.Should().Be(joined);
        json.RootElement.TryGetProperty("date_joined", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("joinedAt", out _).Should().BeFalse();
    }
}
