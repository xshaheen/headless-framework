// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.GatewayProxy.Requester;

public interface IHttpClientBuilder
{
    /// <summary>
    /// Creates the <see cref="HttpClient" />
    /// </summary>
    IHttpClient Create();
}
