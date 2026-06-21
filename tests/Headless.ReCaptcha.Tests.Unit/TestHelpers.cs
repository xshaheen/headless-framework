// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using Headless.ReCaptcha;
using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Services;
using Headless.ReCaptcha.V2;
using Headless.ReCaptcha.V3;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that returns a canned response and records the request it saw.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        LastRequestUri = request.RequestUri;

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return responder(request);
    }
}

internal static class TestHelpers
{
    public static ReCaptchaOptions Options(string siteKey = "site-key", string siteSecret = "secret") =>
        new()
        {
            SiteKey = siteKey,
            SiteSecret = siteSecret,
            VerifyBaseUrl = "https://www.google.com/",
        };

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static (ReCaptchaSiteVerifyV3 Service, StubHttpMessageHandler Handler) CreateV3(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ReCaptchaOptions? options = null
    ) => _CreateV3(responder, options ?? Options(), NullLogger<ReCaptchaSiteVerifyV3>.Instance);

    public static (ReCaptchaSiteVerifyV2 Service, StubHttpMessageHandler Handler) CreateV2(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ReCaptchaOptions? options = null
    ) => _CreateV2(responder, options ?? Options(), NullLogger<ReCaptchaSiteVerifyV2>.Instance);

    private static (ReCaptchaSiteVerifyV3, StubHttpMessageHandler) _CreateV3(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ReCaptchaOptions options,
        Microsoft.Extensions.Logging.ILogger<ReCaptchaSiteVerifyV3> logger
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<ReCaptchaOptions>>();
        monitor.Get(SetupReCaptcha.V3Name).Returns(options);
        var handler = new StubHttpMessageHandler(responder);
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.VerifyBaseUrl) };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(SetupReCaptcha.V3Name).Returns(client);

        return (new ReCaptchaSiteVerifyV3(monitor, factory, logger), handler);
    }

    private static (ReCaptchaSiteVerifyV2, StubHttpMessageHandler) _CreateV2(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        ReCaptchaOptions options,
        Microsoft.Extensions.Logging.ILogger<ReCaptchaSiteVerifyV2> logger
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<ReCaptchaOptions>>();
        monitor.Get(SetupReCaptcha.V2Name).Returns(options);
        var handler = new StubHttpMessageHandler(responder);
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.VerifyBaseUrl) };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(SetupReCaptcha.V2Name).Returns(client);

        return (new ReCaptchaSiteVerifyV2(monitor, factory, logger), handler);
    }

    public static IOptionsSnapshot<ReCaptchaOptions> Snapshot(string name, ReCaptchaOptions options)
    {
        var snapshot = Substitute.For<IOptionsSnapshot<ReCaptchaOptions>>();
        snapshot.Get(name).Returns(options);

        return snapshot;
    }

    public static IReCaptchaLanguageCodeProvider LanguageProvider(string code)
    {
        var provider = Substitute.For<IReCaptchaLanguageCodeProvider>();
        provider.GetLanguageCode().Returns(code);

        return provider;
    }

    public static TagHelperContext TagContext() =>
        new(new TagHelperAttributeList(), new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));

    public static TagHelperOutput TagOutput(string tagName) =>
        new(
            tagName,
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

    public static string Render(this TagHelperOutput output)
    {
        var builder = new StringBuilder();
        builder.Append(output.PreElement.GetContent(HtmlEncoder.Default));
        builder.Append(output.Content.GetContent(HtmlEncoder.Default));
        builder.Append(output.PostElement.GetContent(HtmlEncoder.Default));

        return builder.ToString();
    }
}
