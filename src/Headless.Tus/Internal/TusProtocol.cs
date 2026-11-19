// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Net.Http.Headers;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace Headless.Tus.Internal;

/// <summary>The tus protocol header and version the endpoint answers with.</summary>
internal static class TusProtocol
{
    /// <summary>The header that carries the protocol version on a response.</summary>
    public const string ResumableHeader = "Tus-Resumable";

    /// <summary>The protocol version the endpoint speaks.</summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// Refuses the request with the headers the protocol asks of every response: the protocol version, and on a
    /// status request a ban on caching. tusdotnet puts both on its own refusals but leaves them off a failed
    /// authorization, so a refusal made while authorizing adds them here.
    /// </summary>
    public static void Fail(AuthorizeContext context, HttpStatusCode status, string? message = null)
    {
        var headers = context.HttpContext.Response.Headers;
        headers[ResumableHeader] = Version;

        // A cached refusal would keep answering a client after the cause is gone, for example once it signs in.
        if (context.Intent == IntentType.GetFileInfo)
        {
            headers[HeaderNames.CacheControl] = "no-store";
        }

        if (message is null)
        {
            context.FailRequest(status);
        }
        else
        {
            context.FailRequest(status, message);
        }
    }
}
