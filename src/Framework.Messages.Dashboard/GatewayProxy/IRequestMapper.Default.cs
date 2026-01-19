// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Framework.Messages.GatewayProxy;

public class RequestMapper : IRequestMapper
{
    private const string _SchemeDelimiter = "://";
    private readonly string[] _unsupportedHeaders = ["host", "cookie"];

    public async Task<HttpRequestMessage> Map(HttpRequest request)
    {
        try
        {
            var requestMessage = new HttpRequestMessage
            {
                Content = await _MapContent(request),
                Method = _MapMethod(request),
                RequestUri = _MapUri(request),
            };

            _MapHeaders(request, requestMessage);

            return requestMessage;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error when parsing incoming request, exception: {ex.Message}");
        }
    }

    private string _BuildAbsolute(
        string scheme,
        HostString host,
        PathString pathBase = new(),
        PathString path = new(),
        QueryString query = new(),
        FragmentString fragment = new()
    )
    {
        Argument.IsNotNull(scheme);

        var combinedPath = pathBase.HasValue || path.HasValue ? (pathBase + path).ToString() : "/";

        var encodedHost = host.ToString();
        var encodedQuery = query.ToString();
        var encodedFragment = fragment.ToString();

        // PERF: Calculate string length to allocate correct buffer size for StringBuilder.
        var length =
            scheme.Length
            + _SchemeDelimiter.Length
            + encodedHost.Length
            + combinedPath.Length
            + encodedQuery.Length
            + encodedFragment.Length;

        return new StringBuilder(length)
            .Append(scheme)
            .Append(_SchemeDelimiter)
            .Append(encodedHost)
            .Append(combinedPath)
            .Append(encodedQuery)
            .Append(encodedFragment)
            .ToString();
    }

    private string _GetEncodedUrl(HttpRequest request)
    {
        return _BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path, request.QueryString);
    }

    private async Task<HttpContent?> _MapContent(HttpRequest request)
    {
        if (request.Body == null)
        {
            return null;
        }

        var content = new ByteArrayContent(await _ToByteArray(request.Body));

        content.Headers.TryAddWithoutValidation("Content-Type", [request.ContentType]);

        return content;
    }

    private HttpMethod _MapMethod(HttpRequest request)
    {
        return new HttpMethod(request.Method);
    }

    private Uri _MapUri(HttpRequest request)
    {
        return new Uri(_GetEncodedUrl(request));
    }

    private void _MapHeaders(HttpRequest request, HttpRequestMessage requestMessage)
    {
        foreach (var header in request.Headers)
        {
            if (_IsSupportedHeader(header))
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
    }

    private async Task<byte[]> _ToByteArray(Stream stream)
    {
        using (stream)
        {
            using (var memStream = new MemoryStream())
            {
                await stream.CopyToAsync(memStream);
                return memStream.ToArray();
            }
        }
    }

    private bool _IsSupportedHeader(KeyValuePair<string, StringValues> header)
    {
        return !_unsupportedHeaders.Contains(header.Key.ToLower());
    }
}
