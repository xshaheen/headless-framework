// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features.Authentication;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The user and the authentication result of one run. Both are set on the run's own request, never read through
/// to the request that opened the stream, whose principal may since have expired or changed.
/// </summary>
internal sealed class LiveAuthenticationFeatures : IHttpAuthenticationFeature, IAuthenticateResultFeature
{
    private ClaimsPrincipal? _user;
    private AuthenticateResult? _result;

    // The two stay in step the way the authentication middleware's own feature keeps them: a result describes the
    // principal it was produced for and no other.
    public ClaimsPrincipal? User
    {
        get => _user;
        set
        {
            _user = value;
            _result = null;
        }
    }

    public AuthenticateResult? AuthenticateResult
    {
        get => _result;
        set
        {
            _result = value;
            _user = value?.Principal;
        }
    }
}
