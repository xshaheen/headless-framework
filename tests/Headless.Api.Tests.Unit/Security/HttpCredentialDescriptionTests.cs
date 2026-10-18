// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;

namespace Tests.Security;

public sealed class HttpCredentialDescriptionTests : TestBase
{
    [Fact]
    public void should_carry_the_location_and_name_of_each_kind_of_credential()
    {
        HttpCredentialDescription[] credentials =
        [
            HttpCredentialDescription.Header("X-Api-Key", "The key."),
            HttpCredentialDescription.Query("api_key", "The key."),
            HttpCredentialDescription.Authorization("Basic", "The password."),
        ];

        credentials
            .Select(x => (x.Location, x.Name))
            .Should()
            .Equal(
                (HttpCredentialLocation.Header, "X-Api-Key"),
                (HttpCredentialLocation.Query, "api_key"),
                (HttpCredentialLocation.Authorization, "Basic")
            );
    }

    [Theory]
    [InlineData("", "The key.")]
    [InlineData(" ", "The key.")]
    [InlineData("X-Api-Key", "")]
    public void should_reject_a_blank_name_or_description(string name, string description)
    {
        var create = () => HttpCredentialDescription.Header(name, description);

        create.Should().Throw<ArgumentException>();
    }
}
