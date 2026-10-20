// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Identity;

namespace Tests.Identity.Authentication;

public sealed class CredentialDescriptionTests
{
    [Fact]
    public void should_describe_only_the_api_key_header_by_default()
    {
        // given
        var options = new ApiKeyAuthenticationSchemeOptions();

        // when
        var credentials = options.Credentials;

        // then
        credentials
            .Select(x => (x.Location, x.Name, x.Description))
            .Should()
            .Equal((HttpCredentialLocation.Header, "X-Api-Key", "The API key in the X-Api-Key header."));
    }

    [Fact]
    public void should_describe_the_query_fallback_after_the_header_when_it_is_allowed()
    {
        // given
        var options = new ApiKeyAuthenticationSchemeOptions
        {
            ApiKeyHeaderName = "X-Partner-Key",
            AllowApiKeyInQueryString = true,
            ApiKeyParamName = "partner_key",
        };

        // when
        var credentials = options.Credentials;

        // then
        credentials
            .Select(x => (x.Location, x.Name, x.Description))
            .Should()
            .Equal(
                (HttpCredentialLocation.Header, "X-Partner-Key", "The API key in the X-Partner-Key header."),
                (
                    HttpCredentialLocation.Query,
                    "partner_key",
                    "The API key in the partner_key query parameter, read only when the X-Partner-Key header is absent. Servers and proxies log query strings, so prefer the header."
                )
            );
    }

    [Fact]
    public void should_describe_basic_credentials_in_the_authorization_header()
    {
        // given
        var options = new BasicAuthenticationOptions();

        // when
        var credentials = options.Credentials;

        // then
        credentials.Select(x => (x.Location, x.Name)).Should().Equal((HttpCredentialLocation.Authorization, "Basic"));
    }
}
