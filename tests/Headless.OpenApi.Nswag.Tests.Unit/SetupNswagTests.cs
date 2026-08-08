// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi.Nswag;
using Headless.OpenApi.Nswag.OperationProcessors;
using Headless.OpenApi.Nswag.SchemaProcessors;
using Headless.Primitives;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Security;

namespace Tests;

public sealed class SetupNswagTests
{
    [Fact]
    public void should_register_default_processors_security_and_primitive_mappings()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        // when
        services.AddNswagOpenApi(options => options.AddApiKeySecurity = true);
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<OpenApiDocumentRegistration>().Settings;

        // then
        settings
            .OperationProcessors.Should()
            .ContainSingle(processor => processor is ApiExtraInformationOperationProcessor);
        settings
            .OperationProcessors.Should()
            .ContainSingle(processor => processor is UnauthorizedResponseOperationProcessor);
        settings
            .OperationProcessors.Should()
            .ContainSingle(processor => processor is ForbiddenResponseOperationProcessor);
        settings.OperationProcessors.Should().ContainSingle(processor => processor is ProblemDetailsOperationProcessor);
        settings
            .SchemaSettings.SchemaProcessors.Should()
            .ContainSingle(processor => processor is FluentValidationSchemaProcessor);
        settings
            .SchemaSettings.SchemaProcessors.Should()
            .ContainSingle(processor => processor is GenericNullabilitySchemaProcessor);
        settings.DocumentProcessors.OfType<SecurityDefinitionAppender>().Should().HaveCount(2);
        settings.OperationProcessors.OfType<AspNetCoreOperationSecurityScopeProcessor>().Should().HaveCount(2);
        settings.SchemaSettings.TypeMappers.Should().HaveCount(6);
    }

    [Fact]
    public void should_honor_service_provider_callback_and_disabled_optional_features()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var marker = new Marker();
        services.AddSingleton(marker);

        // when
        services.AddNswagOpenApi(
            options =>
            {
                options.AddBearerSecurity = false;
                options.AddApiKeySecurity = false;
                options.AddPrimitiveMappings = false;
            },
            (settings, provider) =>
            {
                settings.Title = provider.GetRequiredService<Marker>().Title;
                settings.GenerateOriginalParameterNames = false;
            }
        );
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<OpenApiDocumentRegistration>().Settings;

        // then
        settings.Title.Should().Be(marker.Title);
        settings.GenerateOriginalParameterNames.Should().BeFalse();
        settings.DocumentProcessors.OfType<SecurityDefinitionAppender>().Should().BeEmpty();
        settings.OperationProcessors.OfType<AspNetCoreOperationSecurityScopeProcessor>().Should().BeEmpty();
        settings.SchemaSettings.TypeMappers.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(MoneyAmount), JsonObjectType.Number, "decimal", false)]
    [InlineData(typeof(MoneyAmount?), JsonObjectType.Number, "decimal", true)]
    [InlineData(typeof(Month), JsonObjectType.Integer, "int32", false)]
    [InlineData(typeof(Month?), JsonObjectType.Integer, "int32", true)]
    [InlineData(typeof(AccountId), JsonObjectType.String, "string", false)]
    [InlineData(typeof(UserId), JsonObjectType.String, "string", false)]
    public void should_map_building_block_primitives(Type type, JsonObjectType schemaType, string format, bool nullable)
    {
        // given
        var settings = new AspNetCoreOpenApiDocumentGeneratorSettings();
        settings.SchemaSettings.AddBuildingBlocksPrimitiveMappings();
        var mapper = settings
            .SchemaSettings.TypeMappers.OfType<PrimitiveTypeMapper>()
            .Single(x => x.MappedType == type);
        var schema = new JsonSchema();

        // when
        mapper.GenerateSchema(schema, null!);

        // then
        schema.Type.Should().Be(schemaType);
        schema.Format.Should().Be(format);
        schema.IsNullableRaw.Should().Be(nullable ? true : null);
    }

    private sealed class Marker
    {
        public string Title { get; } = "Consumer API";
    }
}
