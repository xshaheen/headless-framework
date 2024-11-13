// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using AutoFixture;
using Framework.Payments.Paymob.CashOut.Models;
using Microsoft.Extensions.Options;
using WireMock.Server;

namespace Tests;

public sealed class PaymobCashOutFixture : IDisposable
{
    public PaymobCashOutFixture()
    {
        Server = WireMockServer.Start();
        HttpClient = new HttpClient();
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
    }

    public Fixture AutoFixture { get; } = new();

    public WireMockServer Server { get; }

    public HttpClient HttpClient { get; }

    public PaymobCashOutOptions CashOutOptions { get; }

    public IOptionsMonitor<PaymobCashOutOptions> OptionsAccessor { get; }

    public TimeProvider TimeProvider { get; }

    public void Dispose()
    {
        Server.Stop();
        HttpClient.Dispose();
    }
}
