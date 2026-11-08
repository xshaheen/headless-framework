// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Tests;

#pragma warning disable EXTEXP0013 // The method under test wraps Microsoft's experimental HTTP logging redaction.
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
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;
        options
            .RequestHeadersDataClasses.Should()
            .Contain(HeaderNames.Authorization, HeadlessDataClassifications.Secret);
        options.RequestHeadersDataClasses.Should().Contain(HeaderNames.Cookie, HeadlessDataClassifications.Secret);
        options.ResponseHeadersDataClasses.Should().Contain(HeaderNames.SetCookie, HeadlessDataClassifications.Secret);
    }

    [Fact]
    public void should_apply_each_callback_and_register_the_middleware_once_when_called_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessHttpLoggingRedaction(options =>
            options.RouteParameterDataClasses["userId"] = HeadlessDataClassifications.Identifier
        );
        var countAfterFirst = services.Count;
        services.AddHeadlessHttpLoggingRedaction(options =>
            options.RequestHeadersDataClasses["X-Api-Key"] = HeadlessDataClassifications.Secret
        );

        // then: the second call adds only its callback
        services.Should().HaveCount(countAfterFirst + 1);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingRedactionOptions>>().Value;
        options.RouteParameterDataClasses.Should().Contain("userId", HeadlessDataClassifications.Identifier);
        options.RequestHeadersDataClasses.Should().Contain("X-Api-Key", HeadlessDataClassifications.Secret);
    }
}
#pragma warning restore EXTEXP0013
