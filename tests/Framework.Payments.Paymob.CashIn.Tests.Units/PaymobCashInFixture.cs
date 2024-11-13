// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        CashInOptions = new PaymobCashInOptions
        {
            ApiBaseUrl = Server.Urls[0],
            Hmac = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
        };

        OptionsAccessor = Substitute.For<IOptionsMonitor<PaymobCashInOptions>>();
        OptionsAccessor.CurrentValue.Returns(CashInOptions);
        TimeProvider = TimeProvider.System;
    }

    public Fixture AutoFixture { get; } = new();

    public WireMockServer Server { get; }

    public HttpClient HttpClient { get; }

    public PaymobCashInOptions CashInOptions { get; }

    public IOptionsMonitor<PaymobCashInOptions> OptionsAccessor { get; }

    public TimeProvider TimeProvider { get; }

    public void Dispose()
    {
        Server.Stop();
        HttpClient.Dispose();
    }
}
