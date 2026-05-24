// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Constants;

/// <summary>
/// Constants for OpenTelemetry semantic-convention attribute names (stable semconv 1.x).
/// <see href="https://opentelemetry.io/docs/specs/semconv/"/>.
/// </summary>
/// <remarks>
/// Grouped by attribute prefix to mirror the semconv document. Span attributes (<see cref="Client"/>,
/// <see cref="EndUser"/>) describe individual operations; <see cref="Resource"/> attributes describe the
/// emitting service/host/environment and apply to every signal it produces.
/// </remarks>
[PublicAPI]
public static class HeadlessOpenTelemetryAttributes
{
    /// <summary>
    /// Client-side network identity. Replaces the deprecated <c>http.client_ip</c>.
    /// <see href="https://opentelemetry.io/docs/specs/semconv/attributes-registry/client/"/>.
    /// </summary>
    public static class Client
    {
        /// <summary>
        /// Client address — domain name (if available without reverse DNS), IPv4, or IPv6.
        /// For HTTP servers behind a proxy, populate from the original client (e.g. <c>X-Forwarded-For</c>)
        /// rather than the immediate peer.
        /// </summary>
        /// <example>E.g. <c>client.example.com</c>, <c>83.164.160.102</c>.</example>
        public const string Address = "client.address";

        /// <summary>Client port number.</summary>
        /// <example>E.g. <c>65123</c>.</example>
        public const string Port = "client.port";
    }

    /// <summary>
    /// End-user identity attributes.
    /// <see href="https://opentelemetry.io/docs/specs/semconv/attributes-registry/enduser/"/>.
    /// </summary>
    /// <remarks>
    /// These attributes are <b>opt-in</b> in stable semconv because they typically contain PII. Enable
    /// only when the telemetry backend's access controls and retention satisfy your compliance posture.
    /// Prefer stable opaque identifiers (e.g. the OIDC <c>sub</c> claim) over usernames or email addresses.
    /// </remarks>
    public static class EndUser
    {
        /// <summary>Username, opaque user identifier (e.g. OIDC <c>sub</c>), or <c>client_id</c>.</summary>
        /// <example>E.g. <c>username</c>.</example>
        public const string Id = "enduser.id";

        /// <summary>Actual or assumed role the client is making the request under.</summary>
        /// <example>E.g. <c>admin</c>.</example>
        public const string Role = "enduser.role";

        /// <summary>OAuth scopes or granted authorities the client currently possesses.</summary>
        /// <example>E.g. <c>read:message,write:files</c>.</example>
        public const string Scope = "enduser.scope";
    }

    /// <summary>
    /// Resource-level attributes (attached to the producing service, not to individual spans).
    /// <see href="https://opentelemetry.io/docs/specs/semconv/resource/"/>.
    /// </summary>
    public static class Resource
    {
        /// <summary>Service identity (<c>service.*</c>).</summary>
        public static class Service
        {
            /// <summary>Logical name of the service.</summary>
            /// <example>E.g. <c>shopping-cart</c>.</example>
            public const string Name = "service.name";
        }

        /// <summary>Host identity (<c>host.*</c>).</summary>
        public static class Host
        {
            /// <summary>
            /// Name of the host. On Unix systems it may be what <c>hostname</c> returns, the FQDN,
            /// or a user-specified name.
            /// </summary>
            /// <example>E.g. <c>opentelemetry-test</c>.</example>
            public const string Name = "host.name";
        }

        /// <summary>Deployment metadata (<c>deployment.*</c>).</summary>
        public static class Deployment
        {
            /// <summary>
            /// Name of the deployment environment (aka tier). Replaces the deprecated
            /// <c>deployment.environment</c> in semconv 1.27.
            /// </summary>
            /// <example>E.g. <c>staging</c>, <c>production</c>.</example>
            public const string EnvironmentName = "deployment.environment.name";
        }
    }
}
