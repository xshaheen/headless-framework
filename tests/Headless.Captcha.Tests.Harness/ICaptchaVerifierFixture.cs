// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Per-provider fixture for the cross-provider verifier conformance suite. A provider supplies a verifier wired
/// over the test stub handler plus its vendor-shaped success/rejection siteverify bodies; the conformance base
/// asserts on the normalized <see cref="CaptchaVerifyResult"/>.
/// </summary>
public interface ICaptchaVerifierFixture
{
    /// <summary>Builds a verifier whose HTTP traffic is routed through <paramref name="handler"/>.</summary>
    ICaptchaVerifier CreateVerifier(StubSiteVerifyHandler handler);

    /// <summary>
    /// Builds a verifier like <see cref="CreateVerifier(StubSiteVerifyHandler)"/>, after <paramref name="configureServices"/>
    /// adds services such as a logger provider or a host environment.
    /// </summary>
    ICaptchaVerifier CreateVerifierWithServices(
        StubSiteVerifyHandler handler,
        Action<IServiceCollection> configureServices
    );

    /// <summary>A vendor-shaped siteverify success body (must include hostname and challenge timestamp).</summary>
    string SuccessResponseBody { get; }

    /// <summary>A vendor-shaped siteverify rejection body (well-formed token, invalid, with error codes).</summary>
    string RejectedResponseBody { get; }
}
