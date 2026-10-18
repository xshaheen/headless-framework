// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// The security schemes a document publishes: those <see cref="HeadlessOpenApiOptions"/> configures, then one per
/// credential of every registered authentication scheme whose options implement <see cref="IHttpCredentialSchemeOptions"/>.
/// </summary>
/// <remarks>
/// A discovered scheme takes the authentication scheme's name, and each further credential of that scheme the name
/// followed by its location, such as <c>ApiKeyQuery</c>. A configured scheme wins over a discovered one with the same
/// name. Authentication schemes are read on every call, because <see cref="IAuthenticationSchemeProvider"/> may add
/// or remove them while the application runs.
/// </remarks>
internal static class SecuritySchemeCatalog
{
    public static async Task<IReadOnlyDictionary<string, OpenApiSecurityScheme>> GetAsync(IServiceProvider services)
    {
        var configured = services.GetRequiredService<IOptions<HeadlessOpenApiOptions>>().Value.GetSecuritySchemes();

        if (services.GetService<IAuthenticationSchemeProvider>() is not { } provider)
        {
            return configured;
        }

        Dictionary<string, OpenApiSecurityScheme>? schemes = null;

        foreach (var scheme in await provider.GetAllSchemesAsync().ConfigureAwait(false))
        {
            if (_GetCredentialOptions(services, scheme) is not { Credentials: { Count: > 0 } credentials })
            {
                continue;
            }

            schemes ??= new Dictionary<string, OpenApiSecurityScheme>(configured, StringComparer.Ordinal);

            for (var i = 0; i < credentials.Count; i++)
            {
                var credential = credentials[i];
                var name = i == 0 ? scheme.Name : scheme.Name + credential.Location;
                schemes.TryAdd(name, _ToSecurityScheme(credential));
            }
        }

        return schemes ?? configured;
    }

    private static IHttpCredentialSchemeOptions? _GetCredentialOptions(
        IServiceProvider services,
        AuthenticationScheme scheme
    )
    {
        if (_GetOptionsType(scheme.HandlerType) is not { } optionsType)
        {
            return null;
        }

        // IOptionsMonitor<T> is covariant, so the closed monitor reads as a monitor of object.
        var monitor = services.GetService(typeof(IOptionsMonitor<>).MakeGenericType(optionsType));

        return (monitor as IOptionsMonitor<object>)?.Get(scheme.Name) as IHttpCredentialSchemeOptions;
    }

    // A handler derived from AuthenticationHandler<TOptions> reads its options from IOptionsMonitor<TOptions>, named
    // after the scheme.
    private static Type? _GetOptionsType(Type handlerType)
    {
        for (var type = handlerType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AuthenticationHandler<>))
            {
                var optionsType = type.GetGenericArguments()[0];

                return typeof(IHttpCredentialSchemeOptions).IsAssignableFrom(optionsType) ? optionsType : null;
            }
        }

        return null;
    }

    private static OpenApiSecurityScheme _ToSecurityScheme(HttpCredentialDescription credential)
    {
        return credential.Location switch
        {
            HttpCredentialLocation.Authorization => new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                // HTTP authentication scheme names are case-insensitive; OpenAPI tools expect the registered lowercase form.
                Scheme = credential.Name.ToLowerInvariant(),
                Description = credential.Description,
            },
            HttpCredentialLocation.Query => new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Query,
                Name = credential.Name,
                Description = credential.Description,
            },
            _ => new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = credential.Name,
                Description = credential.Description,
            },
        };
    }
}
