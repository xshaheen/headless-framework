// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Headless.Checks;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Asserts the "cross-tenant answers like a missing id" convention: a request for another tenant's resource must be
/// indistinguishable from a request for an id that does not exist.
/// </summary>
/// <remarks>
/// <para>
/// This is an application convention, not a framework invariant. The framework collapses only tenant-resolution
/// rejections to 404 (<c>g:tenant_resolution_failed</c>). It answers a tenant write-guard refusal with 409
/// <c>g:cross_tenant_write</c> and a request with no tenant context with 403 <c>g:tenant_required</c>. An
/// application that adopts the convention makes each endpoint answer 404 for another tenant's id, typically because
/// the tenant query filter hides the row before any authorization check can confirm it exists.
/// </para>
/// <para>
/// Send both requests from one client authenticated as the probing tenant (tenant B): the cross-tenant request
/// targets an id tenant A owns, the missing-id request targets an id no tenant owns.
/// </para>
/// </remarks>
[PublicAPI]
public static class TenantIsolationHttpAssertions
{
    // ProblemDetails members the framework fills per request; they differ between any two responses.
    private static readonly string[] _DefaultIgnoredMembers = ["traceId", "timestamp", "instance"];

    // Codes are literals so this package does not reference Headless.Api.Core or Headless.MultiTenancy.
    private const string _TenantRequiredCode = "g:tenant_required";
    private const string _CrossTenantWriteCode = "g:cross_tenant_write";

    /// <summary>Top-level JSON members removed from both bodies before they are compared.</summary>
    public static IReadOnlyList<string> DefaultIgnoredMembers => _DefaultIgnoredMembers;

    /// <summary>
    /// Sends a GET for <paramref name="crossTenantUri"/> and one for <paramref name="missingUri"/>, and asserts both
    /// answer 404 with the same content type and the same body once per-request members are removed.
    /// </summary>
    /// <param name="client">A client authenticated as the probing tenant.</param>
    /// <param name="crossTenantUri">A URI naming a resource another tenant owns.</param>
    /// <param name="missingUri">A URI naming a resource that does not exist.</param>
    /// <param name="ignoredMembers">
    /// Top-level JSON members to ignore in addition to <see cref="DefaultIgnoredMembers"/>, such as an application's
    /// own correlation id.
    /// </param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that faults with an assertion failure when the convention does not hold.</returns>
    public static Task ShouldAnswerNotFoundAcrossTenantsAsync(
        HttpClient client,
        string crossTenantUri,
        string missingUri,
        IEnumerable<string>? ignoredMembers = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(crossTenantUri);
        Argument.IsNotNullOrWhiteSpace(missingUri);

        return ShouldAnswerNotFoundAcrossTenantsAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, crossTenantUri),
            () => new HttpRequestMessage(HttpMethod.Get, missingUri),
            ignoredMembers,
            cancellationToken
        );
    }

    /// <summary>
    /// Sends the request <paramref name="crossTenantRequest"/> builds and the one <paramref name="missingRequest"/>
    /// builds, and asserts both answer 404 with the same content type and the same body once per-request members
    /// are removed. Use this overload for verbs other than GET or requests with a body.
    /// </summary>
    /// <param name="client">A client authenticated as the probing tenant.</param>
    /// <param name="crossTenantRequest">Builds a request for a resource another tenant owns.</param>
    /// <param name="missingRequest">Builds the same request for a resource that does not exist.</param>
    /// <param name="ignoredMembers">
    /// Top-level JSON members to ignore in addition to <see cref="DefaultIgnoredMembers"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that faults with an assertion failure when the convention does not hold.</returns>
    public static async Task ShouldAnswerNotFoundAcrossTenantsAsync(
        HttpClient client,
        Func<HttpRequestMessage> crossTenantRequest,
        Func<HttpRequestMessage> missingRequest,
        IEnumerable<string>? ignoredMembers = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(client);
        Argument.IsNotNull(crossTenantRequest);
        Argument.IsNotNull(missingRequest);

        string[] ignored = [.. _DefaultIgnoredMembers, .. ignoredMembers ?? []];

        // The control runs first: when a missing id is not a 404, an equal cross-tenant answer proves nothing.
        var missing = await _SendAsync(client, missingRequest, cancellationToken).ConfigureAwait(false);

        if (missing.Status != HttpStatusCode.NotFound)
        {
            _Fail(
                $"Expected the missing-id control {missing.Target} to answer 404, but it answered "
                    + $"{_Format(missing.Status)}. The route does not answer 404 for an id that does not exist, so "
                    + "comparing the cross-tenant answer with it proves nothing."
            );
        }

        var cross = await _SendAsync(client, crossTenantRequest, cancellationToken).ConfigureAwait(false);

        if (cross.Status != HttpStatusCode.NotFound)
        {
            _Fail(_DescribeStatus(cross));
        }

        if (!string.Equals(cross.MediaType, missing.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            _Fail(
                $"Expected {cross.Target} to answer like the missing id {missing.Target}, but the content types "
                    + $"differ: \"{cross.MediaType}\" versus \"{missing.MediaType}\". A difference lets a caller tell "
                    + "another tenant's id from a missing one."
            );
        }

        var crossBody = _Normalize(cross.Body, ignored);
        var missingBody = _Normalize(missing.Body, ignored);

        if (!_BodiesEqual(crossBody, missingBody))
        {
            _Fail(
                $"Expected {cross.Target} to answer like the missing id {missing.Target}, but the 404 bodies differ "
                    + $"after removing [{string.Join(", ", ignored)}]. A difference lets a caller tell another "
                    + $"tenant's id from a missing one.{Environment.NewLine}Cross-tenant: {_Display(crossBody)}"
                    + $"{Environment.NewLine}Missing id:   {_Display(missingBody)}"
            );
        }
    }

    private static string _DescribeStatus(Answer cross)
    {
        var code = _ErrorCodes(cross.Body);
        var status = _Format(cross.Status);
        var prefix = $"Expected {cross.Target} to answer 404 like a missing id, but it answered {status}";

        if ((int)cross.Status is >= 200 and < 300)
        {
            return $"{prefix}: it returned another tenant's resource.";
        }

        if (cross.Status == HttpStatusCode.Forbidden && code.Contains(_TenantRequiredCode, StringComparer.Ordinal))
        {
            return $"{prefix} {_TenantRequiredCode}. That is the framework's answer to a request with no tenant "
                + "context, not a cross-tenant leak: the request never reached the cross-tenant lookup. Authenticate "
                + "the client as the probing tenant so the request carries a tenant.";
        }

        if (cross.Status == HttpStatusCode.Conflict && code.Contains(_CrossTenantWriteCode, StringComparer.Ordinal))
        {
            return $"{prefix} {_CrossTenantWriteCode}. That is the framework's tenant write guard refusing the "
                + "write, not the 404 convention: the endpoint reached another tenant's row before the guard stopped "
                + "it. Load the row through the tenant query filter so the endpoint answers 404 first.";
        }

        if (cross.Status == HttpStatusCode.Forbidden)
        {
            return $"{prefix}. A 403 confirms the resource exists in another tenant: an existence leak under the "
                + "404 convention.";
        }

        return $"{prefix}.";
    }

    private static async Task<Answer> _SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken
    )
    {
        using var request = createRequest();
        var target = $"{request.Method} {request.RequestUri}";
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new Answer(target, response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
    }

    private static object _Normalize(string body, string[] ignored)
    {
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        if (node is JsonObject json)
        {
            foreach (var member in ignored)
            {
                json.Remove(member);
            }
        }

        return (object?)node ?? body;
    }

    private static bool _BodiesEqual(object cross, object missing)
    {
        return (cross, missing) switch
        {
            (JsonNode a, JsonNode b) => JsonNode.DeepEquals(a, b),
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string _Display(object body) => body is JsonNode node ? node.ToJsonString() : $"\"{body}\"";

    // Reads the framework's single `error.code` descriptor and every `errors[].code` entry.
    private static List<string> _ErrorCodes(string body)
    {
        List<string> codes = [];

        try
        {
            if (JsonNode.Parse(body) is not JsonObject json)
            {
                return codes;
            }

            if (json["error"] is JsonObject error && error["code"] is JsonValue single)
            {
                codes.Add(single.ToString());
            }

            if (json["errors"] is JsonArray errors)
            {
                foreach (var entry in errors)
                {
                    if (entry is JsonObject item && item["code"] is JsonValue value)
                    {
                        codes.Add(value.ToString());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A non-JSON body carries no framework code.
        }

        return codes;
    }

    private static string _Format(HttpStatusCode status) =>
        ((int)status).ToString(CultureInfo.InvariantCulture) + " " + status;

    [DoesNotReturn]
    private static void _Fail(string message)
    {
        AssertionEngine.TestFramework.Throw(message);
        throw new InvalidOperationException(message);
    }

    private sealed record Answer(string Target, HttpStatusCode Status, string? MediaType, string Body);
}
