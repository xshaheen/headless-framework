// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.IO.Compression;
using FileSignatures;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Diagnostics;
using Headless.Api.Identity.Normalizer;
using Headless.Api.Identity.Schemes;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Checks;
using Headless.Constants;
using Headless.Core;
using Headless.Serializer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Headless.Api;

[PublicAPI]
public static class ApiSetup
{
    private const string _StringEncryptionSectionName = "Headless:StringEncryption";
    private const string _StringHashSectionName = "Headless:StringHash";

    public static readonly FileFormatInspector FileFormatInspector = new(FileFormatLocator.GetFormats());

    public static void ConfigureGlobalSettings()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        JsonWebTokenHandler.DefaultMapInboundClaims = false;
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
    }

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddHeadlessFramework()
        {
            Argument.IsNotNull(builder);

            builder._AddDefaultStringEncryptionService();
            builder._AddDefaultStringHashService();

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessFramework(
            IConfiguration stringEncryptionConfig,
            IConfiguration stringHashConfig
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(stringEncryptionConfig);
            Argument.IsNotNull(stringHashConfig);

            builder.Services.AddStringEncryptionService(stringEncryptionConfig);
            builder.Services.AddStringHashService(stringHashConfig);

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessFramework(
            Action<StringEncryptionOptions> configureEncryption,
            Action<StringHashOptions>? configureHash = null
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(configureEncryption);

            builder.Services.AddStringEncryptionService(configureEncryption);

            if (configureHash is null)
            {
                builder._AddDefaultStringHashService();
            }
            else
            {
                builder.Services.AddStringHashService(configureHash);
            }

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessFramework(
            Action<StringEncryptionOptions, IServiceProvider> configureEncryption,
            Action<StringHashOptions, IServiceProvider>? configureHash = null
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(configureEncryption);

            builder.Services.AddStringEncryptionService(configureEncryption);

            if (configureHash is null)
            {
                builder._AddDefaultStringHashService();
            }
            else
            {
                builder.Services.AddStringHashService(configureHash);
            }

            return builder._AddCore();
        }

        private void _AddDefaultStringEncryptionService()
        {
            builder.Services.AddStringEncryptionService(
                builder.Configuration.GetRequiredSection(_StringEncryptionSectionName)
            );
        }

        private void _AddDefaultStringHashService()
        {
            builder.Services.AddStringHashService(builder.Configuration.GetRequiredSection(_StringHashSectionName));
        }

        private WebApplicationBuilder _AddCore()
        {
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddResilienceEnricher();
            builder.Services.AddHeadlessJsonService();
            builder.Services.AddHeadlessTimeService();
            builder.Services.AddHeadlessApiResponseCompression();
            builder.Services.AddHeadlessProblemDetails();
            builder.Services.ConfigureHeadlessDefaultApi();

            builder.Services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
            builder.Services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));
            builder.Services.TryAddSingleton<IEnumLocaleAccessor, DefaultEnumLocaleAccessor>();
            builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
            builder.Services.TryAddSingleton<IApplicationInformationAccessor, ApplicationInformationAccessor>();
            builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();

            builder.Services.TryAddSingleton<IPasswordGenerator, PasswordGenerator>();
            builder.Services.TryAddSingleton<IFileFormatInspector>(FileFormatInspector);
            builder.Services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
            builder.Services.TryAddSingleton<IContentTypeProvider, ExtendedFileExtensionContentTypeProvider>();

            builder.Services.TryAddSingleton<IClaimsPrincipalFactory, ClaimsPrincipalFactory>();
            builder.Services.TryAddSingleton<IJwtTokenFactory, JwtTokenFactory>();

            builder.Services.TryAddSingleton<ICurrentLocale, CurrentCultureCurrentLocale>();
            builder.Services.TryAddSingleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>();
            builder.Services.TryAddSingleton<ICurrentUser, HttpCurrentUser>();
            builder.Services.TryAddSingleton<ICurrentTimeZone, LocalCurrentTimeZone>();
            builder.Services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            // AddOrReplace (not TryAdd) so the real ambient-tenant resolver always wins over
            // any fallback (e.g. Headless.Messaging.Core's NullCurrentTenant) regardless of
            // package registration order. Mirrors MultiTenancySetup.AddHeadlessMultiTenancy.
            builder.Services.AddOrReplaceSingleton<ICurrentTenant, CurrentTenant>();
            builder.Services.TryAddSingleton<IWebClientInfoProvider, HttpWebClientInfoProvider>();

            builder.Services.TryAddScoped<IRequestContext, HttpRequestContext>();
            builder.Services.TryAddScoped<IAbsoluteUrlFactory, HttpAbsoluteUrlFactory>();
            builder.Services.TryAddScoped<IRequestedApiVersion, HttpContextRequestedApiVersion>();

            builder.Services.AddOrReplaceSingleton<ILookupNormalizer, HeadlessLookupNormalizer>();
            builder.Services.AddOrReplaceSingleton<
                IAuthenticationSchemeProvider,
                DynamicAuthenticationSchemeProvider
            >();

            // Turn on resilience by default
            builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            return builder;
        }
    }
}
