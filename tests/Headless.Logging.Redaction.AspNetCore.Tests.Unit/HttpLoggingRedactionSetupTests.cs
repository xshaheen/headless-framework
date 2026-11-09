// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Tests;

public sealed class HttpLoggingRedactionSetupTests : TestBase
{
    [Fact]
    public void should_classify_the_credential_headers_as_secret()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessHttpLoggingRedaction();

        // then
        using var provider = services.BuildServiceProvider();
        var options = _MicrosoftOptions(provider);
        options
            .RequestHeadersDataClasses.Should()
            .Contain(HeaderNames.Authorization, HeadlessDataClassifications.Secret);
        options.RequestHeadersDataClasses.Should().Contain(HeaderNames.Cookie, HeadlessDataClassifications.Secret);
        options.ResponseHeadersDataClasses.Should().Contain(HeaderNames.SetCookie, HeadlessDataClassifications.Secret);
    }

    [Fact]
    public void should_map_excluded_paths_and_unmatched_routes()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessHttpLoggingRedaction(options =>
        {
            options.ExcludePathStartsWith.Add("/health");
            options.IncludeUnmatchedRoutes = true;
        });

        // then
        using var provider = services.BuildServiceProvider();
        var options = _MicrosoftOptions(provider);
        options.ExcludePathStartsWith.Should().Contain("/health");
        options.IncludeUnmatchedRoutes.Should().BeTrue();
    }

    [Fact]
    public void should_apply_each_callback_and_map_the_options_once_when_called_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessHttpLoggingRedaction(options =>
            options.RouteParameters["userId"] = HeadlessDataClassifications.Identifier
        );
        services.AddHeadlessHttpLoggingRedaction(options =>
            options.RequestHeaders["X-Api-Key"] = HeadlessDataClassifications.Secret
        );

        // then: both callbacks reach Microsoft's options, mapped once
        services
            .Should()
            .ContainSingle(d =>
                d.ServiceType.Name == "IConfigureOptions`1"
                && d.ImplementationType != null
                && d.ImplementationType.Name == "HttpLoggingRedactionOptionsSetup"
            );
        using var provider = services.BuildServiceProvider();
        var options = _MicrosoftOptions(provider);
        options.RouteParameterDataClasses.Should().Contain("userId", HeadlessDataClassifications.Identifier);
        options.RequestHeadersDataClasses.Should().Contain("X-Api-Key", HeadlessDataClassifications.Secret);
    }

    // Only the assertions read Microsoft's experimental type; the calls under test compile without a suppression.
#pragma warning disable EXTEXP0013 // Reads the mapping's result to check it.
    private static LoggingRedactionOptions _MicrosoftOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;
#pragma warning restore EXTEXP0013
}
