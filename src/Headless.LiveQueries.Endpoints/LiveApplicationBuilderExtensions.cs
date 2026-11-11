// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.LiveQueries.Internal;
using Microsoft.AspNetCore.Builder;

namespace Headless.LiveQueries;

/// <summary>Middleware of live queries.</summary>
[PublicAPI]
public static class LiveApplicationBuilderExtensions
{
    private const string _CommandsMarker = "Headless.LiveQueries.Commands";

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds command acknowledgement: a request that carries <see cref="LiveCommandHeaders.Session" /> and
        /// <see cref="LiveCommandHeaders.Command" /> has the tracked writes it commits acknowledged, by command id,
        /// on the streams the same client session and user hold, and its response carries
        /// <see cref="LiveCommandHeaders.Acknowledgement" />. A request without both headers passes through
        /// untouched. Streams need no middleware; a host that acknowledges no command leaves this out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Place it after <c>UseAuthentication()</c> and before the endpoints, beside <c>UseAuthorization()</c>. The
        /// user is read when the request arrives here, so ahead of authentication every command would count as
        /// anonymous and a signed-in client's streams would acknowledge nothing.
        /// </para>
        /// <para>
        /// A write is the command's when it is announced on the request's own execution flow before the request
        /// ends. A commit completed elsewhere, such as an ambient <c>TransactionScope</c> that finishes on another
        /// thread or work the handler leaves running, is announced as nobody's command.
        /// </para>
        /// <para>Calling it again adds nothing.</para>
        /// </remarks>
        /// <returns>The same application builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException">The application builder is <see langword="null" />.</exception>
        public IApplicationBuilder UseHeadlessLiveCommands()
        {
            Argument.IsNotNull(app);

            // Twice in one pipeline, the outer one would see no write and overwrite the inner one's header.
            if (!app.Properties.TryAdd(_CommandsMarker, value: true))
            {
                return app;
            }

            return app.UseMiddleware<LiveCommandMiddleware>();
        }
    }
}
