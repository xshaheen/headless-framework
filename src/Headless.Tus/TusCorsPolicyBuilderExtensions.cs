// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Cors.Infrastructure;
using tusdotnet.Helpers;

namespace Headless.Tus;

/// <summary>CORS policy helpers for tus endpoints.</summary>
[PublicAPI]
public static class TusCorsPolicyBuilderExtensions
{
    extension(CorsPolicyBuilder policy)
    {
        /// <summary>
        /// Applies the tus protocol's CORS surface to the policy: allowed request headers,
        /// exposed response headers, and allowed methods (see <see cref="TusCorsDefaults"/>).
        /// Origins and credentials remain the caller's decision.
        /// </summary>
        /// <returns>the same builder for chaining</returns>
        public CorsPolicyBuilder WithTusHeaders()
        {
            return policy
                .WithHeaders([.. TusCorsDefaults.AllowedHeaders])
                .WithExposedHeaders([.. TusCorsDefaults.ExposedHeaders])
                .WithMethods([.. TusCorsDefaults.AllowedMethods]);
        }
    }
}
