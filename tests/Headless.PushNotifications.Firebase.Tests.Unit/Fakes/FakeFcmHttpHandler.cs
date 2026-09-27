// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Http;

namespace Tests.Fakes;

/// <summary>One FCM v1 send the fake handler received.</summary>
/// <param name="Target">
/// The <c>message.fid</c>, <c>message.token</c>, <c>message.topic</c>, or <c>message.condition</c> the request addressed.
/// </param>
/// <param name="Attempt">1-based count of requests for this target, including this one.</param>
/// <param name="ReceivedAt">The fake clock's time when the request arrived.</param>
/// <param name="Body">The raw JSON request body.</param>
internal sealed record FcmRecordedRequest(string Target, int Attempt, DateTimeOffset ReceivedAt, JsonObject Body);

/// <summary>
/// Stands in for both Google endpoints the FirebaseAdmin SDK calls: it answers the OAuth2 token exchange with a
/// fixed access token and passes every FCM v1 <c>messages:send</c> request to a scripted responder, recording it.
/// Only the transport is fake; the SDK's own retry, error mapping, and batch fan-out run for real on top of it.
/// </summary>
internal sealed class FakeFcmHttpHandler(TimeProvider timeProvider) : HttpMessageHandler
{
    private const string _FcmErrorType = "type.googleapis.com/google.firebase.fcm.v1.FcmError";

    private readonly ConcurrentQueue<FcmRecordedRequest> _requests = new();
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private int _tokenRequests;

    /// <summary>
    /// Produces the response for one FCM send. It may throw to simulate a transport failure, and it receives the
    /// request's cancellation token so a test can hold a request open until the caller cancels.
    /// </summary>
    public Func<FcmRecordedRequest, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        static (request, _) => Task.FromResult(Success(request.Target));

    /// <summary>
    /// Produces the OAuth2 token-exchange response. Defaults to a valid access token; a test replaces it to make the
    /// credential exchange fail.
    /// </summary>
    public Func<HttpResponseMessage> TokenResponder { get; set; } =
        static () =>
            _Json(
                HttpStatusCode.OK,
                """{"access_token":"fake-access-token","expires_in":3600,"token_type":"Bearer"}"""
            );

    public IReadOnlyList<FcmRecordedRequest> Requests => [.. _requests];

    public int TokenRequests => Volatile.Read(ref _tokenRequests);

    public IReadOnlyList<FcmRecordedRequest> RequestsFor(string target)
    {
        return [.. _requests.Where(r => string.Equals(r.Target, target, StringComparison.Ordinal))];
    }

    /// <summary>Waits in real time, bounded, until at least <paramref name="count"/> FCM sends have arrived.</summary>
    public async Task WaitForRequestsAsync(int count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (_requests.Count < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    public static HttpResponseMessage Success(string target)
    {
        // Built as a node so a condition target's quotes are escaped.
        return _Json(
            HttpStatusCode.OK,
            new JsonObject { ["name"] = $"projects/test-project/messages/{target}" }.ToJsonString()
        );
    }

    /// <summary>A google.rpc error body carrying an FCM error code, with the HTTP status FCM pairs it with.</summary>
    public static HttpResponseMessage Error(string fcmErrorCode, TimeSpan? retryAfter = null)
    {
        var (status, rpcStatus) = fcmErrorCode switch
        {
            "UNREGISTERED" => (HttpStatusCode.NotFound, "NOT_FOUND"),
            "SENDER_ID_MISMATCH" => (HttpStatusCode.Forbidden, "PERMISSION_DENIED"),
            "INVALID_ARGUMENT" => (HttpStatusCode.BadRequest, "INVALID_ARGUMENT"),
            "QUOTA_EXCEEDED" => (HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED"),
            "INTERNAL" => (HttpStatusCode.InternalServerError, "INTERNAL"),
            "UNAVAILABLE" => (HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"),
            "THIRD_PARTY_AUTH_ERROR" => (HttpStatusCode.Unauthorized, "UNAUTHENTICATED"),
            _ => throw new ArgumentOutOfRangeException(nameof(fcmErrorCode), fcmErrorCode, null),
        };

        var body = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = (int)status,
                ["message"] = $"Simulated {fcmErrorCode}",
                ["status"] = rpcStatus,
                ["details"] = new JsonArray(new JsonObject { ["@type"] = _FcmErrorType, ["errorCode"] = fcmErrorCode }),
            },
        };

        var response = _Json(status, body.ToJsonString());

        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        }

        return response;
    }

    /// <summary>A google.rpc error body with no FCM error code, so the SDK derives only the platform code.</summary>
    public static HttpResponseMessage PlatformError(HttpStatusCode status, string rpcStatus)
    {
        var body = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = (int)status,
                ["message"] = $"Simulated {rpcStatus}",
                ["status"] = rpcStatus,
            },
        };

        return _Json(status, body.ToJsonString());
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (string.Equals(request.RequestUri?.Host, "oauth2.googleapis.com", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _tokenRequests);

            return TokenResponder();
        }

        var json = await request.Content!.ReadAsStringAsync(cancellationToken);
        var body = JsonNode.Parse(json)!.AsObject();
        var message = body["message"]!.AsObject();
        var target =
            (string?)message["fid"]
            ?? (string?)message["token"]
            ?? (string?)message["topic"]
            ?? (string?)message["condition"]
            ?? "<none>";
        var attempt = _attempts.AddOrUpdate(target, 1, static (_, count) => count + 1);
        var recorded = new FcmRecordedRequest(target, attempt, timeProvider.GetUtcNow(), body);

        _requests.Enqueue(recorded);

        return await Responder(recorded, cancellationToken);
    }

    private static HttpResponseMessage _Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Routes every HTTP client the FirebaseAdmin SDK and its credential create to one fake handler.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
    {
        return new NonDisposingHandler(handler);
    }

    // The SDK disposes its clients with the app; the shared fake must outlive each of them.
    private sealed class NonDisposingHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new(inner, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return _invoker.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>A throwaway service-account credential whose RSA key is generated per process.</summary>
internal static class FakeServiceAccount
{
    public const string ProjectId = "test-project";

    public static string Json { get; } = _Create();

    private static string _Create()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);

        return JsonSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["type"] = "service_account",
                ["project_id"] = ProjectId,
                ["private_key_id"] = "fake-key-id",
                ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["client_email"] = "fake@test-project.iam.gserviceaccount.com",
                ["client_id"] = "1",
                ["token_uri"] = "https://oauth2.googleapis.com/token",
            }
        );
    }
}
