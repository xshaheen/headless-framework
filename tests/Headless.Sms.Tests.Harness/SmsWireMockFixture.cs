// Copyright (c) Mahmoud Shaheen. All rights reserved.

using WireMock.Server;

namespace Headless.Sms.Testing;

/// <summary>
/// xUnit class fixture exposing a running <see cref="WireMockServer"/> plus an <see cref="IHttpClientFactory"/>
/// whose clients reach it, for SMS HTTP-provider unit tests. Point the provider's endpoint option at
/// <see cref="BaseUrl"/> and stub responses on <see cref="Server"/>. Call <see cref="Reset"/> from each test
/// (the fixture is shared across a test class) before configuring stubs and asserting request counts.
/// </summary>
public sealed class SmsWireMockFixture : IDisposable
{
    public SmsWireMockFixture()
    {
        Server = WireMockServer.Start();
        HttpClientFactory = new WireMockHttpClientFactory();
    }

    /// <summary>The mock HTTP server.</summary>
    public WireMockServer Server { get; }

    /// <summary>A factory whose clients send absolute-URL requests that reach <see cref="Server"/>.</summary>
    public IHttpClientFactory HttpClientFactory { get; }

    /// <summary>The base URL of <see cref="Server"/> (no trailing slash).</summary>
    public string BaseUrl => Server.Urls[0];

    /// <summary>Clears all stubs and request logs. Call at the start of each test.</summary>
    public void Reset()
    {
        Server.Reset();
    }

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
    }

    // A fresh client per call: providers create-and-dispose their client (`using var`), and every client
    // reaches the server via the absolute endpoint URLs the test points at BaseUrl.
    private sealed class WireMockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
