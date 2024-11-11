// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models;
using Microsoft.Extensions.Options;
using WireMock.Server;

namespace Tests;

public sealed class PaymobCashInFixture : IDisposable
{
    public PaymobCashInFixture()
    {
        Server = WireMockServer.Start();
        HttpClient = new HttpClient();
        AutoFixture.Register(() => JsonSerializer.Deserialize<object?>("null"));

        CashInConfig = new PaymobCashInOptions
        {
            ApiBaseUrl = Server.Urls[0],
            Hmac = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
        };

        Options = Substitute.For<IOptionsMonitor<PaymobCashInOptions>>();
        Options.CurrentValue.Returns(CashInConfig);
        TimeProvider = TimeProvider.System;
    }

    public Fixture AutoFixture { get; } = new();

    public WireMockServer Server { get; }

    public HttpClient HttpClient { get; }

    public PaymobCashInOptions CashInConfig { get; }

    public IOptionsMonitor<PaymobCashInOptions> Options { get; }

    public TimeProvider TimeProvider { get; }

    public void Dispose()
    {
        Server.Stop();
        HttpClient.Dispose();
    }
}
