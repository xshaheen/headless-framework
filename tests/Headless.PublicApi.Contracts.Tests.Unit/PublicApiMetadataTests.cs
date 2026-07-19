// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Api.Security.Jwt;
using Headless.AuditLog;
using Headless.Caching;
using Headless.Couchbase.Context;
using Headless.Features.Models;
using Headless.Jobs;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Redis;
using Headless.Security;
using Headless.Settings.Models;
using Headless.Sitemaps;
using NetTopologySuite.Geometries;
using StackExchange.Redis;

namespace Tests;

public sealed class PublicApiMetadataTests
{
    [Fact]
    public void request_and_options_properties_should_preserve_nullability_metadata()
    {
        var nullability = new NullabilityInfoContext();
        var properties = new (Type Type, string PropertyName, NullabilityState Expected)[]
        {
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.Action), NullabilityState.NotNull),
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.EntityType), NullabilityState.Nullable),
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.EntityId), NullabilityState.Nullable),
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.Data), NullabilityState.Nullable),
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.ErrorCode), NullabilityState.Nullable),
            (typeof(AuditLogQuery), nameof(AuditLogQuery.Action), NullabilityState.Nullable),
            (typeof(AuditLogQuery), nameof(AuditLogQuery.EntityType), NullabilityState.Nullable),
            (typeof(AuditLogQuery), nameof(AuditLogQuery.EntityId), NullabilityState.Nullable),
            (typeof(AuditLogQuery), nameof(AuditLogQuery.UserId), NullabilityState.Nullable),
            (typeof(AuditLogQuery), nameof(AuditLogQuery.TenantId), NullabilityState.Nullable),
            (
                typeof(SettingDefinitionCreateOptions),
                nameof(SettingDefinitionCreateOptions.Name),
                NullabilityState.NotNull
            ),
            (
                typeof(SettingDefinitionCreateOptions),
                nameof(SettingDefinitionCreateOptions.DefaultValue),
                NullabilityState.Nullable
            ),
            (
                typeof(SettingDefinitionCreateOptions),
                nameof(SettingDefinitionCreateOptions.DisplayName),
                NullabilityState.Nullable
            ),
            (
                typeof(SettingDefinitionCreateOptions),
                nameof(SettingDefinitionCreateOptions.Description),
                NullabilityState.Nullable
            ),
            (
                typeof(FeatureDefinitionCreateOptions),
                nameof(FeatureDefinitionCreateOptions.Name),
                NullabilityState.NotNull
            ),
            (
                typeof(FeatureDefinitionCreateOptions),
                nameof(FeatureDefinitionCreateOptions.DefaultValue),
                NullabilityState.Nullable
            ),
            (
                typeof(FeatureDefinitionCreateOptions),
                nameof(FeatureDefinitionCreateOptions.DisplayName),
                NullabilityState.Nullable
            ),
            (
                typeof(FeatureDefinitionCreateOptions),
                nameof(FeatureDefinitionCreateOptions.Description),
                NullabilityState.Nullable
            ),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Token), NullabilityState.NotNull),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.SigningKey), NullabilityState.NotNull),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Issuer), NullabilityState.NotNull),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Audience), NullabilityState.NotNull),
            (
                typeof(JwtTokenValidationRequest),
                nameof(JwtTokenValidationRequest.EncryptingKey),
                NullabilityState.Nullable
            ),
            (typeof(SitemapUrlOptions), nameof(SitemapUrlOptions.Images), NullabilityState.Nullable),
            (
                typeof(SitemapUrlOptions),
                nameof(SitemapUrlOptions.WriteAlternateLanguageCodes),
                NullabilityState.Nullable
            ),
            (typeof(CashInBillingData), nameof(CashInBillingData.Email), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.FirstName), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.LastName), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.PhoneNumber), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.Country), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.State), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.City), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.Apartment), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.Street), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.Floor), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.Building), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.ShippingMethod), NullabilityState.NotNull),
            (typeof(CashInBillingData), nameof(CashInBillingData.PostalCode), NullabilityState.NotNull),
        };

        foreach (var (type, propertyName, expected) in properties)
        {
            var property = type.GetProperty(propertyName);

            property.Should().NotBeNull();
            nullability.Create(property!).ReadState.Should().Be(expected);
        }
    }

    [Fact]
    public void request_and_options_with_mandatory_values_should_use_required_initializers()
    {
        var properties = new (Type Type, string PropertyName)[]
        {
            (typeof(AuditLogWriteRequest), nameof(AuditLogWriteRequest.Action)),
            (typeof(SettingDefinitionCreateOptions), nameof(SettingDefinitionCreateOptions.Name)),
            (typeof(FeatureDefinitionCreateOptions), nameof(FeatureDefinitionCreateOptions.Name)),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Token)),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.SigningKey)),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Issuer)),
            (typeof(JwtTokenValidationRequest), nameof(JwtTokenValidationRequest.Audience)),
        };

        foreach (var (type, propertyName) in properties)
        {
            var property = type.GetProperty(propertyName);

            property.Should().NotBeNull();
            property!.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull();
            property.SetMethod.Should().NotBeNull();
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Should().Contain(typeof(IsExternalInit));
        }

        foreach (var type in properties.Select(property => property.Type).Distinct())
        {
            var constructors = type.GetConstructors();

            constructors.Should().ContainSingle();
            constructors[0].GetParameters().Should().BeEmpty();
        }
    }

    [Fact]
    public void constructor_parameters_should_preserve_non_null_metadata()
    {
        var nullability = new NullabilityInfoContext();
        var constructor = _GetPublicConstructor(
            typeof(CashInBillingData),
            [typeof(string), typeof(string), typeof(string), typeof(string)]
        );

        foreach (var parameter in constructor.GetParameters())
        {
            nullability.Create(parameter).ReadState.Should().Be(NullabilityState.NotNull);
        }
    }

    [Fact]
    public void sitemap_options_should_remain_nullable_and_optional()
    {
        var nullability = new NullabilityInfoContext();
        var constructors = new[]
        {
            _GetPublicConstructor(typeof(SitemapUrl), [typeof(Uri), typeof(SitemapUrlOptions)]),
            _GetPublicConstructor(
                typeof(SitemapUrl),
                [typeof(IEnumerable<SitemapAlternateUrl>), typeof(SitemapUrlOptions)]
            ),
        };

        foreach (var options in constructors.Select(constructor => constructor.GetParameters()[^1]))
        {
            options.Name.Should().Be("options");
            options.HasDefaultValue.Should().BeTrue();
            options.DefaultValue.Should().BeNull();
            nullability.Create(options).ReadState.Should().Be(NullabilityState.Nullable);
        }
    }

    [Fact]
    public void normalized_async_apis_should_expose_trailing_optional_cancellation_tokens()
    {
        var contracts = new (Type Type, string MethodName, int GenericArity, Type[] ParameterTypes)[]
        {
            (
                typeof(IFactoryCacheStore),
                nameof(IFactoryCacheStore.TryGetEntryAsync),
                1,
                [typeof(string), typeof(FactoryCacheReadOptions), typeof(CancellationToken)]
            ),
            (
                typeof(IFactoryCacheStore),
                nameof(IFactoryCacheStore.TryGetAllEntriesAsync),
                1,
                [typeof(IReadOnlyList<string>), typeof(FactoryCacheReadOptions), typeof(CancellationToken)]
            ),
            (
                typeof(INodeDiscoveryProvider),
                nameof(INodeDiscoveryProvider.GetNodesAsync),
                0,
                [typeof(string), typeof(CancellationToken)]
            ),
            (
                typeof(INodeDiscoveryProvider),
                nameof(INodeDiscoveryProvider.GetNodeAsync),
                0,
                [typeof(string), typeof(string), typeof(CancellationToken)]
            ),
            (
                typeof(INodeDiscoveryProvider),
                nameof(INodeDiscoveryProvider.RegisterNodeAsync),
                0,
                [typeof(CancellationToken)]
            ),
            (
                typeof(INodeDiscoveryProvider),
                nameof(INodeDiscoveryProvider.GetNamespacesAsync),
                0,
                [typeof(CancellationToken)]
            ),
            (
                typeof(INodeDiscoveryProvider),
                nameof(INodeDiscoveryProvider.ListServicesAsync),
                0,
                [typeof(string), typeof(CancellationToken)]
            ),
            (
                typeof(IPaymobCashOutBroker),
                nameof(IPaymobCashOutBroker.DisburseAsync),
                0,
                [typeof(CashOutDisburseRequest), typeof(CancellationToken)]
            ),
            (
                typeof(ConnectionMultiplexerExtensions),
                nameof(ConnectionMultiplexerExtensions.CountAllKeysAsync),
                0,
                [typeof(IConnectionMultiplexer), typeof(CancellationToken)]
            ),
        };

        foreach (var (type, methodName, genericArity, parameterTypes) in contracts)
        {
            var method = _GetPublicMethod(type, methodName, genericArity, parameterTypes);
            var cancellationToken = method.GetParameters()[^1];

            method.Name.Should().EndWith("Async");
            cancellationToken.Name.Should().Be("cancellationToken");
            cancellationToken.ParameterType.Should().Be<CancellationToken>();
            cancellationToken.HasDefaultValue.Should().BeTrue();
        }
    }

    [Fact]
    public void factory_cache_read_options_should_precede_cancellation_token()
    {
        foreach (
            var methodName in new[]
            {
                nameof(IFactoryCacheStore.TryGetEntryAsync),
                nameof(IFactoryCacheStore.TryGetAllEntriesAsync),
            }
        )
        {
            var firstParameterType = string.Equals(
                methodName,
                nameof(IFactoryCacheStore.TryGetEntryAsync),
                StringComparison.Ordinal
            )
                ? typeof(string)
                : typeof(IReadOnlyList<string>);
            var parameters = _GetPublicMethod(
                    typeof(IFactoryCacheStore),
                    methodName,
                    1,
                    [firstParameterType, typeof(FactoryCacheReadOptions), typeof(CancellationToken)]
                )
                .GetParameters();

            parameters[^2].ParameterType.Should().Be<FactoryCacheReadOptions>();
            parameters[^2].HasDefaultValue.Should().BeTrue();
            parameters[^2].DefaultValue.Should().BeNull();
            parameters[^1].ParameterType.Should().Be<CancellationToken>();
        }
    }

    [Fact]
    public void job_function_delegate_should_preserve_consumer_facing_parameter_order()
    {
        var parameters = typeof(JobFunctionDelegate)
            .GetMethod(
                nameof(JobFunctionDelegate.Invoke),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                binder: null,
                [typeof(IServiceProvider), typeof(Headless.Jobs.Base.JobFunctionContext), typeof(CancellationToken)],
                modifiers: null
            )!
            .GetParameters();

        parameters
            .Select(parameter => parameter.Name)
            .Should()
            .Equal("serviceProvider", "context", "cancellationToken");
        parameters
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(IServiceProvider), typeof(Headless.Jobs.Base.JobFunctionContext), typeof(CancellationToken));
    }

    [Fact]
    public void normalized_namespaces_and_extension_holder_should_be_public_contracts()
    {
        foreach (
            var securityType in new[]
            {
                typeof(IStringHashService),
                typeof(IStringEncryptionService),
                typeof(StringHashOptions),
                typeof(StringEncryptionOptions),
            }
        )
        {
            securityType.Namespace.Should().Be("Headless.Security");
        }

        typeof(HeadlessGeometryExtensions).IsPublic.Should().BeTrue();
        typeof(HeadlessGeometryExtensions).IsAbstract.Should().BeTrue();
        typeof(HeadlessGeometryExtensions).IsSealed.Should().BeTrue();
        typeof(DocumentSetExtensions)
            .GetMethods()
            .Should()
            .Contain(method => string.Equals(method.Name, "GetAllReplicasAsync", StringComparison.Ordinal));
    }

    private static ConstructorInfo _GetPublicConstructor(Type type, Type[] parameterTypes)
    {
        return type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                parameterTypes,
                modifiers: null
            ) ?? throw new InvalidOperationException($"Expected public constructor was not found on {type.FullName}.");
    }

    private static MethodInfo _GetPublicMethod(
        Type type,
        string methodName,
        int genericArity,
        IReadOnlyList<Type> parameterTypes
    )
    {
        return type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
            )
            .Single(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetGenericArguments().Length == genericArity
                && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes)
            );
    }
}
