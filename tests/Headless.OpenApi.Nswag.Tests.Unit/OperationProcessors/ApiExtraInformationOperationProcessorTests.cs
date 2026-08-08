// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Headless.OpenApi.Nswag.OperationProcessors;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using NSwag;
using NSwag.Generation.AspNetCore;

namespace Tests.OperationProcessors;

public sealed class ApiExtraInformationOperationProcessorTests
{
    private readonly ApiExtraInformationOperationProcessor _sut = new();

    [Fact]
    public void should_preserve_deprecation_and_enrich_response_and_parameter_metadata()
    {
        // given
        var operation = new OpenApiOperation { IsDeprecated = true };
        var response = new OpenApiResponse();
        response.Content["application/json"] = new OpenApiMediaType();
        response.Content["text/plain"] = new OpenApiMediaType();
        operation.Responses["200"] = response;
        operation.Parameters.Add(new OpenApiParameter { Name = "limit", Schema = new NJsonSchema.JsonSchema() });

        var apiDescription = new ApiDescription { ActionDescriptor = new ActionDescriptor() };
        var responseType = new ApiResponseType { StatusCode = 200 };
        responseType.ApiResponseFormats.Add(new ApiResponseFormat { MediaType = "application/json" });
        apiDescription.SupportedResponseTypes.Add(responseType);
        apiDescription.ParameterDescriptions.Add(
            new ApiParameterDescription
            {
                Name = "LIMIT",
                DefaultValue = 25,
                IsRequired = true,
                ModelMetadata = _GetLimitMetadata(),
            }
        );
        var context = _CreateContext(operation, apiDescription);

        // when
        var result = _sut.Process(context);

        // then
        result.Should().BeTrue();
        operation.IsDeprecated.Should().BeTrue();
        response.Content.Keys.Should().Equal("application/json");
        var parameter = operation.Parameters.Should().ContainSingle().Which;
        parameter.Description.Should().Be("Maximum number of orders");
        parameter.Schema.Default.Should().Be("25");
        parameter.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void should_preserve_existing_parameter_metadata_and_ignore_unknown_parameters()
    {
        // given
        var operation = new OpenApiOperation();
        operation.Parameters.Add(
            new OpenApiParameter
            {
                Name = "limit",
                Description = "consumer description",
                Schema = new NJsonSchema.JsonSchema { Default = "10" },
            }
        );
        operation.Parameters.Add(new OpenApiParameter { Name = "unknown", Schema = new NJsonSchema.JsonSchema() });
        var apiDescription = new ApiDescription { ActionDescriptor = new ActionDescriptor() };
        apiDescription.ParameterDescriptions.Add(
            new ApiParameterDescription
            {
                Name = "limit",
                DefaultValue = 25,
                ModelMetadata = _GetLimitMetadata(),
            }
        );
        var context = _CreateContext(operation, apiDescription);

        // when
        _sut.Process(context);

        // then
        operation.Parameters[0].Description.Should().Be("consumer description");
        operation.Parameters[0].Schema.Default.Should().Be("10");
        operation.Parameters[1].Description.Should().BeNull();
        operation.Parameters[1].Schema.Default.Should().BeNull();
    }

    [Fact]
    public void should_filter_default_response_content_types()
    {
        // given
        var operation = new OpenApiOperation();
        var response = new OpenApiResponse();
        response.Content["application/problem+json"] = new OpenApiMediaType();
        response.Content["text/html"] = new OpenApiMediaType();
        operation.Responses["default"] = response;
        var apiDescription = new ApiDescription { ActionDescriptor = new ActionDescriptor() };
        var responseType = new ApiResponseType { IsDefaultResponse = true };
        responseType.ApiResponseFormats.Add(new ApiResponseFormat { MediaType = "application/problem+json" });
        apiDescription.SupportedResponseTypes.Add(responseType);

        // when
        _sut.Process(_CreateContext(operation, apiDescription));

        // then
        response.Content.Keys.Should().Equal("application/problem+json");
    }

    private static readonly MethodInfo _Method = typeof(object).GetMethod(
        nameof(ToString),
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
        Type.EmptyTypes
    )!;

    private static ModelMetadata _GetLimitMetadata()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForProperty(typeof(QueryRequest), nameof(QueryRequest.Limit));
    }

    private static AspNetCoreOperationProcessorContext _CreateContext(
        OpenApiOperation operation,
        ApiDescription apiDescription
    )
    {
        var description = new OpenApiOperationDescription
        {
            Operation = operation,
            Path = "/orders",
            Method = "GET",
        };

        return new AspNetCoreOperationProcessorContext(
            new OpenApiDocument(),
            description,
            typeof(object),
            _Method,
            null!,
            null!,
            null!,
            [description]
        )
        {
            ApiDescription = apiDescription,
        };
    }

    private sealed class QueryRequest
    {
        [Display(Description = "Maximum number of orders")]
        public int Limit { get; init; }
    }
}
