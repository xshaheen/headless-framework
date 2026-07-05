// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Stubs the transport of a Headless SMS provider's named HttpClient. Because each named SMS instance owns
/// an HttpClient registration named <c>"Headless:{Provider}Sms:{name}"</c> (the default uses
/// <c>"Headless:{Provider}Sms"</c>), stubbing that exact name proves a send exercised real options binding
/// and keyed resolution end to end — not a hand-built sender.
/// </summary>
public static class NamedSmsHttpStub
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Replaces the primary handler of the named HttpClient with a <see cref="CapturingHttpMessageHandler"/>
        /// answering <paramref name="statusCode"/>/<paramref name="responseBody"/>. Call before or after the
        /// provider registers the client — named HttpClient configuration merges by name.
        /// </summary>
        /// <param name="httpClientName">The exact HttpClient name (for example <c>"Headless:VodafoneSms:otp"</c>).</param>
        /// <param name="statusCode">The canned response status.</param>
        /// <param name="responseBody">The canned response body (match the provider's success shape when asserting success).</param>
        /// <returns>The handler, for asserting on captured requests.</returns>
        public CapturingHttpMessageHandler StubSmsHttpClient(
            string httpClientName,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string responseBody = "{}"
        )
        {
            var handler = new CapturingHttpMessageHandler(statusCode, responseBody);
            services.AddHttpClient(httpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

            return handler;
        }
    }
}
