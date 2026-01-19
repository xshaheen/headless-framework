// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.GatewayProxy.Requester;

public class HttpClientHttpRequester(ILoggerFactory loggerFactory, IHttpClientCache cacheHandlers) : IHttpRequester
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<HttpClientHttpRequester>();

    public async Task<HttpResponseMessage> GetResponse(HttpRequestMessage request)
    {
        var builder = new HttpClientBuilder();

        var cacheKey = _GetCacheKey(request);

        var httpClient = _GetHttpClient(cacheKey, builder);

        try
        {
            return await httpClient.SendAsync(request);
        }
        catch (Exception exception)
        {
            _logger.LogError("Error making http request, exception:" + exception.Message);
            throw;
        }
        finally
        {
            cacheHandlers.Set(cacheKey, httpClient, TimeSpan.FromHours(24));
        }
    }

    private IHttpClient _GetHttpClient(string cacheKey, IHttpClientBuilder builder)
    {
        var httpClient = cacheHandlers.Get(cacheKey);

        if (httpClient == null)
        {
            httpClient = builder.Create();
        }

        return httpClient;
    }

    private static string _GetCacheKey(HttpRequestMessage request)
    {
        Ensure.True(request.RequestUri is not null);

        var baseUrl = $"{request.RequestUri.Scheme}://{request.RequestUri.Authority}";

        return baseUrl;
    }
}
