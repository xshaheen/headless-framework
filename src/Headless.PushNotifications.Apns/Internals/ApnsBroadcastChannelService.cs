// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Calls APNs' channel-management endpoint with the instance's credentials, following Apple's "Sending channel
/// management requests to APNs".
/// </summary>
internal sealed class ApnsBroadcastChannelService(
    IHttpClientFactory httpClientFactory,
    string httpClientName,
    IApnsAuthenticator authenticator,
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? optionsName,
    ILogger<ApnsBroadcastChannelService> logger
) : IApnsBroadcastChannelService
{
    private const string _ChannelIdHeader = "apns-channel-id";
    private const string _RequestIdHeader = "apns-request-id";

    // Apple's create body names the channel's push type "LiveActivity", the only value it allows.
    private const string _ChannelPushType = "LiveActivity";

    private static readonly MediaTypeHeaderValue _JsonContentType = new("application/json");

    public async ValueTask<ApnsBroadcastChannel> CreateAsync(
        ApnsChannelStoragePolicy storagePolicy,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsInEnum(storagePolicy);

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["message-storage-policy"] = (int)storagePolicy,
                ["push-type"] = _ChannelPushType,
            }
        );

        var answer = await _SendAsync("create", HttpMethod.Post, "channels", channelId: null, body, cancellationToken)
            .ConfigureAwait(false);

        _EnsureStatus("create", answer, HttpStatusCode.Created);

        if (string.IsNullOrWhiteSpace(answer.ChannelId))
        {
            throw new ApnsRequestException(
                "APNs created the channel but its answer carried no apns-channel-id header.",
                answer.Status,
                reason: null,
                answer.RequestId
            );
        }

        return new ApnsBroadcastChannel(answer.ChannelId, storagePolicy);
    }

    public async ValueTask<ApnsBroadcastChannel> GetAsync(
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(channelId);

        var answer = await _SendAsync("read", HttpMethod.Get, "channels", channelId, body: null, cancellationToken)
            .ConfigureAwait(false);

        _EnsureStatus("read", answer, HttpStatusCode.OK);

        using var document = JsonDocument.Parse(answer.Body);
        var policy = document.RootElement.GetProperty("message-storage-policy").GetInt32();

        return new ApnsBroadcastChannel(channelId, (ApnsChannelStoragePolicy)policy);
    }

    public async ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var answer = await _SendAsync(
                "list",
                HttpMethod.Get,
                "all-channels",
                channelId: null,
                body: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        _EnsureStatus("list", answer, HttpStatusCode.OK);

        using var document = JsonDocument.Parse(answer.Body);

        return document.RootElement.TryGetProperty("channels", out var channels)
            ? [.. channels.EnumerateArray().Select(static c => c.GetString()!)]
            : [];
    }

    public async ValueTask DeleteAsync(string channelId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(channelId);

        var answer = await _SendAsync("delete", HttpMethod.Delete, "channels", channelId, body: null, cancellationToken)
            .ConfigureAwait(false);

        _EnsureStatus("delete", answer, HttpStatusCode.NoContent);
    }

    private async ValueTask<ChannelAnswer> _SendAsync(
        string operation,
        HttpMethod method,
        string resource,
        string? channelId,
        byte[]? body,
        CancellationToken cancellationToken
    )
    {
        var options = optionsMonitor.Get(optionsName);
        var client = httpClientFactory.CreateClient(httpClientName);

        // The request carries the bearer provider token in token mode, so cleartext is refused off loopback.
        ApnsPushNotificationService.EnsureSecureEndpoint(client.BaseAddress);

        var requestId = Guid.NewGuid().ToString("D");
        var credential = await authenticator.GetCredentialAsync(options, cancellationToken).ConfigureAwait(false);

        var answer = await _PostAsync(
                client,
                method,
                options.BundleId,
                resource,
                channelId,
                body,
                requestId,
                credential,
                cancellationToken
            )
            .ConfigureAwait(false);

        // The same one-shot renewal as a push: channel requests authenticate with the same provider token.
        if (
            answer.Status == HttpStatusCode.Forbidden
            && string.Equals(answer.Reason, ApnsResponseMapper.ExpiredProviderTokenReason, StringComparison.Ordinal)
            && await authenticator.RenewExpiredAsync(options, credential, cancellationToken).ConfigureAwait(false)
                is { } retryCredential
        )
        {
            logger.LogProviderTokenExpired(credential.Generation, retryCredential.Generation);

            answer = await _PostAsync(
                    client,
                    method,
                    options.BundleId,
                    resource,
                    channelId,
                    body,
                    requestId,
                    retryCredential,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (answer.Reason is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogChannelRequestRejected(
                operation,
                (int)answer.Status,
                answer.Reason,
                answer.RequestId ?? requestId
            );
        }

        return answer;
    }

    private static async ValueTask<ChannelAnswer> _PostAsync(
        HttpClient client,
        HttpMethod method,
        string bundleId,
        string resource,
        string? channelId,
        byte[]? body,
        string requestId,
        ApnsCredential credential,
        CancellationToken cancellationToken
    )
    {
        using var message = new HttpRequestMessage(
            method,
            new Uri($"/1/apps/{Uri.EscapeDataString(bundleId)}/{resource}", UriKind.Relative)
        );

        message.Version = HttpVersion.Version20;
        message.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        credential.Apply(message);
        message.Headers.TryAddWithoutValidation(_RequestIdHeader, requestId);

        if (channelId is not null)
        {
            message.Headers.TryAddWithoutValidation(_ChannelIdHeader, channelId);
        }

        if (body is not null)
        {
            message.Content = new ByteArrayContent(body) { Headers = { ContentType = _JsonContentType } };
        }

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var answeredRequestId = _Header(response.Headers, _RequestIdHeader) ?? requestId;
        var reason = response.IsSuccessStatusCode ? null : ApnsResponseMapper.ReadError(responseBody).Reason;

        return new ChannelAnswer(
            response.StatusCode,
            reason,
            _Header(response.Headers, _ChannelIdHeader),
            answeredRequestId,
            responseBody
        );
    }

    private static void _EnsureStatus(string operation, ChannelAnswer answer, HttpStatusCode expected)
    {
        if (answer.Status == expected)
        {
            return;
        }

        var reason = answer.Reason ?? "no reason";

        throw new ApnsRequestException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"APNs rejected the channel {operation} with HTTP {(int)answer.Status} ({reason})."
            ),
            answer.Status,
            answer.Reason,
            answer.RequestId
        );
    }

    private static string? _Header(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private readonly record struct ChannelAnswer(
        HttpStatusCode Status,
        string? Reason,
        string? ChannelId,
        string? RequestId,
        byte[] Body
    );
}
