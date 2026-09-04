// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.OpenApi.Nswag;
using Headless.Testing.Tests;
using NJsonSchema.Generation;
using NSwag;
using NSwag.Generation;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Contexts;

namespace Tests.OperationProcessors;

public sealed class IfMatchOperationProcessorTests : TestBase
{
    [Fact]
    public void should_document_required_header_for_annotated_operations()
    {
        var context = _CreateContext(nameof(_ProtectedEndpoint));

        new IfMatchOperationProcessor().Process(context);

        context
            .OperationDescription.Operation.Parameters.Should()
            .ContainSingle(x => x.Name == "If-Match" && x.IsRequired);
        context.OperationDescription.Operation.Responses.Should().ContainKey("428");
    }

    [Fact]
    public void should_leave_unannotated_operations_unchanged()
    {
        var context = _CreateContext(nameof(_OpenEndpoint));

        new IfMatchOperationProcessor().Process(context);

        context.OperationDescription.Operation.Parameters.Should().BeEmpty();
    }

    private static OperationProcessorContext _CreateContext(string methodName)
    {
        var document = new OpenApiDocument();
        var description = new OpenApiOperationDescription { Operation = new OpenApiOperation() };
        var settings = new AspNetCoreOpenApiDocumentGeneratorSettings();
        var resolver = new JsonSchemaResolver(new NJsonSchema.JsonSchema(), settings.SchemaSettings);
        return new OperationProcessorContext(
            document,
            description,
            typeof(IfMatchOperationProcessorTests),
            typeof(IfMatchOperationProcessorTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!,
            new OpenApiDocumentGenerator(settings, resolver),
            resolver,
            settings,
            [description]
        );
    }

    [RequireIfMatch]
    private static void _ProtectedEndpoint() { }

    private static void _OpenEndpoint() { }
}
