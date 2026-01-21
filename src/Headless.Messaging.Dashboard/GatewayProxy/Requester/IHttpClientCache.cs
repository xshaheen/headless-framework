// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.GatewayProxy.Requester;

public interface IHttpClientCache
{
    bool Exists(string id);

    IHttpClient? Get(string id);

    void Remove(string id);

    void Set(string id, IHttpClient handler, TimeSpan expirationTime);
}
