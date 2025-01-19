// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Constants;

public static class JwtClaimTypes
{
    /// <summary>Subject - Identifier for the End-User at the Issuer.</summary>
    public const string Subject = "sub";

    /// <summary>Audience(s) that this ID Token is intended for. It MUST contain the OAuth 2.0 client_id of the Relying Party as an audience value. It MAY also contain identifiers for other audiences. In the general case, the aud value is an array of case sensitive strings. In the common special case when there is one audience, the aud value MAY be a single case sensitive string.</summary>
    public const string Audience = "aud";

    /// <summary>Issuer Identifier for the Issuer of the response. The iss value is a case sensitive URL using the https scheme that contains scheme, host, and optionally, port number and path components and no query or fragment components.</summary>
    public const string Issuer = "iss";

    /// <summary>The time before which the JWT MUST NOT be accepted for processing, specified as the number of seconds from 1970-01-01T0:0:0Z</summary>
    public const string NotBefore = "nbf";

    /// <summary>The iat (issued at) claim identifies the time at which the JWT was issued, , specified as the number of seconds from 1970-01-01T0:0:0Z</summary>
    public const string IssuedAt = "iat";

    /// <summary>The exp (expiration time) claim identifies the expiration time on or after which the token MUST NOT be accepted for processing, specified as the number of seconds from 1970-01-01T0:0:0Z</summary>
    public const string Expiration = "exp";

    /// <summary>
    /// Authentication Context Class Reference. String specifying an Authentication Context Class Reference value that identifies the Authentication Context Class that the authentication performed satisfied.
    /// The value "0" indicates the End-User authentication did not meet the requirements of ISO/IEC 29115 level 1.
    /// Authentication using a long-lived browser cookie, for instance, is one example where the use of "level 0" is appropriate.
    /// Authentications with level 0 SHOULD NOT be used to authorize access to any resource of any monetary value.
    ///  (This corresponds to the OpenID 2.0 PAPE nist_auth_level 0.)
    /// An absolute URI or an RFC 6711 registered name SHOULD be used as the acr value; registered names MUST NOT be used with a different meaning than that which is registered.
    /// Parties using this claim will need to agree upon the meanings of the values used, which may be context-specific.
    /// The acr value is a case sensitive string.
    /// </summary>
    public const string AuthenticationContextClassReference = "acr";

    /// <summary>Time when the End-User authentication occurred. Its value is a JSON number representing the number of seconds from 1970-01-01T0:0:0Z as measured in UTC until the date/time. When a max_age request is made or when auth_time is requested as an Essential Claim, then this Claim is REQUIRED; otherwise, its inclusion is OPTIONAL.</summary>
    public const string AuthenticationTime = "auth_time";

    /// <summary>The party to which the ID Token was issued. If present, it MUST contain the OAuth 2.0 Client ID of this party. This Claim is only needed when the ID Token has a single audience value and that audience is different than the authorized party. It MAY be included even when the authorized party is the same as the sole audience. The azp value is a case sensitive string containing a StringOrURI value.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary> Access Token hash value. Its value is the base64url encoding of the left-most half of the hash of the octets of the ASCII representation of the access_token value, where the hash algorithm used is the hash algorithm used in the alg Header Parameter of the ID Token's JOSE Header. For instance, if the alg is RS256, hash the access_token value with SHA-256, then take the left-most 128 bits and base64url encode them. The at_hash value is a case sensitive string.</summary>
    public const string AccessTokenHash = "at_hash";

    /// <summary>Code hash value. Its value is the base64url encoding of the left-most half of the hash of the octets of the ASCII representation of the code value, where the hash algorithm used is the hash algorithm used in the alg Header Parameter of the ID Token's JOSE Header. For instance, if the alg is HS512, hash the code value with SHA-512, then take the left-most 256 bits and base64url encode them. The c_hash value is a case sensitive string.</summary>
    public const string AuthorizationCodeHash = "c_hash";

    /// <summary>State hash value. Its value is the base64url encoding of the left-most half of the hash of the octets of the ASCII representation of the state value, where the hash algorithm used is the hash algorithm used in the alg Header Parameter of the ID Token's JOSE Header. For instance, if the alg is HS512, hash the code value with SHA-512, then take the left-most 256 bits and base64url encode them. The c_hash value is a case sensitive string.</summary>
    public const string StateHash = "s_hash";

    /// <summary>String value used to associate a Client session with an ID Token, and to mitigate replay attacks. The value is passed through unmodified from the Authentication Request to the ID Token. If present in the ID Token, Clients MUST verify that the nonce Claim Value is equal to the value of the nonce parameter sent in the Authentication Request. If present in the Authentication Request, Authorization Servers MUST include a nonce Claim in the ID Token with the Claim Value being the nonce value sent in the Authentication Request. Authorization Servers SHOULD perform no other processing on nonce values used. The nonce value is a case sensitive string.</summary>
    public const string Nonce = "nonce";

    /// <summary>JWT ID. A unique identifier for the token, which can be used to prevent reuse of the token. These tokens MUST only be used once, unless conditions for reuse were negotiated between the parties; any such negotiation is beyond the scope of this specification.</summary>
    public const string JwtId = "jti";

    /// <summary>OAuth 2.0 Client Identifier valid at the Authorization Server.</summary>
    public const string ClientId = "client_id";

    /// <summary>OpenID Connect requests MUST contain the "openid" scope value. If the openid scope value is not present, the behavior is entirely unspecified. Other scope values MAY be present. Scope values used that are not understood by an implementation SHOULD be ignored.</summary>
    public const string Scope = "scope";

    /// <summary>The "act" (actor) claim provides a means within a JWT to express that delegation has occurred and identify the acting party to whom authority has been delegated.The "act" claim value is a JSON object and members in the JSON object are claims that identify the actor. The claims that make up the "act" claim identify and possibly provide additional information about the actor.</summary>
    public const string Actor = "act";

    /// <summary>The "may_act" claim makes a statement that one party is authorized to become the actor and act on behalf of another party. The claim value is a JSON object and members in the JSON object are claims that identify the party that is asserted as being eligible to act for the party identified by the JWT containing the claim.</summary>
    public const string MayAct = "may_act";
}
