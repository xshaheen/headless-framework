// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
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
    /// Refuses the request with the protocol version on the response. tusdotnet leaves the version off a failed
    /// authorization, and puts it on every other refusal, so it is added here.
    /// </summary>
    public static void Fail(AuthorizeContext context, HttpStatusCode status, string? message = null)
    {
        context.HttpContext.Response.Headers[ResumableHeader] = Version;

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
