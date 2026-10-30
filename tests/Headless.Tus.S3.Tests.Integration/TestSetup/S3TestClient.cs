// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;

namespace Tests.TestSetup;

/// <summary>
/// An S3 client whose every request passes through its own <see cref="S3RequestGate" />, configured the way the docs
/// tell an application to configure one for a non-AWS endpoint: path style, an explicit authentication region, and
/// checksums only where an operation requires them.
/// </summary>
public sealed class S3TestClient : IDisposable
{
    private readonly AmazonS3Client _client;

    internal S3TestClient(string serviceUrl, string accessKey, string secretKey, string region, TusS3Provider provider)
    {
        Gate = new S3RequestGate();
        Provider = provider;

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            HttpClientFactory = new GatedHttpClientFactory(Gate),
        };

        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public IAmazonS3 S3 => _client;

    internal S3RequestGate Gate { get; }

    /// <summary>The provider preset a store using this client must apply.</summary>
    public TusS3Provider Provider { get; }

    public void Dispose()
    {
        _client.Dispose();
        Gate.Dispose();
    }

    // Hands the SDK one HttpClient that sends through the gate; the gate's lifetime belongs to the S3TestClient.
    private sealed class GatedHttpClientFactory(S3RequestGate gate) : HttpClientFactory
    {
        private readonly Lazy<HttpClient> _httpClient = new(() => new HttpClient(gate, disposeHandler: false));

        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return _httpClient.Value;
        }

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig)
        {
            return false;
        }

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig)
        {
            return false;
        }
    }
}
