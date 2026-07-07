// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AutoFixture;
using Headless.Payments.Paymob.CashOut.Models;
using Microsoft.Extensions.Options;
using WireMock.Server;
using WireMock.Settings;

namespace Tests;

public sealed class PaymobCashOutFixture : IDisposable
{
    public PaymobCashOutFixture()
    {
        // Paymob's transaction inquiry is a GET with a JSON body; WireMock only parses
        // bodies for non-GET methods unless this setting is enabled.
        Server = WireMockServer.Start(new WireMockServerSettings { AllowBodyForAllHttpMethods = true });
        HttpClient = new HttpClient { BaseAddress = new Uri(Server.Urls[0]) };
        AutoFixture.Register(() => JsonSerializer.Deserialize<object?>("null"));
        CashOutOptions = new PaymobCashOutOptions
        {
            ApiBaseUrl = Server.Urls[0],
            UserName = "username",
            Password = "password",
            ClientId = "client_id",
            ClientSecret = "client_secret",
        };
        OptionsAccessor = Substitute.For<IOptionsMonitor<PaymobCashOutOptions>>();
        OptionsAccessor.CurrentValue.Returns(CashOutOptions);
        TimeProvider = TimeProvider.System;
        HttpClientFactory = Substitute.For<IHttpClientFactory>();
        HttpClientFactory.CreateClient(Arg.Any<string>()).Returns(HttpClient);
    }

    public Fixture AutoFixture { get; } = new();

    public WireMockServer Server { get; }

    public HttpClient HttpClient { get; }

    public IHttpClientFactory HttpClientFactory { get; }

    public PaymobCashOutOptions CashOutOptions { get; }

    public IOptionsMonitor<PaymobCashOutOptions> OptionsAccessor { get; }

    public TimeProvider TimeProvider { get; }

    public void Dispose()
    {
        Server.Stop();
        HttpClient.Dispose();
    }
}
