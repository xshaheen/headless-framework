using System;
// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api.OperationProcessors;
using Headless.Testing.Tests;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace Tests.OperationProcessors;

public sealed class CamelCaseQueryParameterOperationProcessorTests : TestBase
{
    private readonly CamelCaseQueryParameterOperationProcessor _sut = new();

    [Fact]
    public void should_convert_pascal_case_query_parameter_to_camel_case()
    {
        // given
        var context = _CreateContext(new OpenApiParameter { Name = "PageSize", Kind = OpenApiParameterKind.Query });

        // when
        _sut.Process(context);

        // then
        context.OperationDescription.Operation.Parameters[0].Name.Should().Be("pageSize");
    }

    [Fact]
    public void should_convert_multiple_query_parameters()
    {
        // given
        var context = _CreateContext(
            new OpenApiParameter { Name = "PageSize", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "SortOrder", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "SearchTerm", Kind = OpenApiParameterKind.Query }
        );

        // when
        _sut.Process(context);

        // then
        var parameters = context.OperationDescription.Operation.Parameters;
        parameters[0].Name.Should().Be("pageSize");
        parameters[1].Name.Should().Be("sortOrder");
        parameters[2].Name.Should().Be("searchTerm");
    }

    [Fact]
    public void should_not_change_odata_parameters()
    {
        // given
        var context = _CreateContext(
            new OpenApiParameter { Name = "$filter", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "$top", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "$orderby", Kind = OpenApiParameterKind.Query }
        );

        // when
        _sut.Process(context);

        // then
        var parameters = context.OperationDescription.Operation.Parameters;
        parameters[0].Name.Should().Be("$filter");
        parameters[1].Name.Should().Be("$top");
        parameters[2].Name.Should().Be("$orderby");
    }

    [Fact]
    public void should_not_change_non_query_parameters()
    {
        // given
        var context = _CreateContext(
            new OpenApiParameter { Name = "Authorization", Kind = OpenApiParameterKind.Header },
            new OpenApiParameter { Name = "UserId", Kind = OpenApiParameterKind.Path }
        );

        // when
        _sut.Process(context);

        // then
        var parameters = context.OperationDescription.Operation.Parameters;
        parameters[0].Name.Should().Be("Authorization");
        parameters[1].Name.Should().Be("UserId");
    }

    [Fact]
    public void should_handle_mixed_parameter_kinds()
    {
        // given
        var context = _CreateContext(
            new OpenApiParameter { Name = "UserId", Kind = OpenApiParameterKind.Path },
            new OpenApiParameter { Name = "PageSize", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "$filter", Kind = OpenApiParameterKind.Query },
            new OpenApiParameter { Name = "Accept", Kind = OpenApiParameterKind.Header }
        );

        // when
        _sut.Process(context);

        // then
        var parameters = context.OperationDescription.Operation.Parameters;
        parameters[0].Name.Should().Be("UserId");
        parameters[1].Name.Should().Be("pageSize");
        parameters[2].Name.Should().Be("$filter");
        parameters[3].Name.Should().Be("Accept");
    }

    [Fact]
    public void should_leave_already_camel_case_parameters_unchanged()
    {
        // given
        var context = _CreateContext(new OpenApiParameter { Name = "pageSize", Kind = OpenApiParameterKind.Query });

        // when
        _sut.Process(context);

        // then
        context.OperationDescription.Operation.Parameters[0].Name.Should().Be("pageSize");
    }

    [Fact]
    public void should_handle_operation_with_no_parameters()
    {
        // given
        var operation = new OpenApiOperation();
        var desc = new OpenApiOperationDescription
        {
            Operation = operation,
            Path = "/test",
            Method = "GET",
        };
        var context = new OperationProcessorContext(
            new OpenApiDocument(),
            desc,
            typeof(object),
            typeof(object).GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            )!,
            null!,
            null!,
            null!,
            [desc]
        );

        // when
        var result = _sut.Process(context);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_always_return_true()
    {
        // given
        var context = _CreateContext(new OpenApiParameter { Name = "Test", Kind = OpenApiParameterKind.Query });

        // when
        var result = _sut.Process(context);

        // then
        result.Should().BeTrue();
    }

    #region Helpers

    private static OperationProcessorContext _CreateContext(params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation();

        foreach (var parameter in parameters)
        {
            operation.Parameters.Add(parameter);
        }

        var desc = new OpenApiOperationDescription
        {
            Operation = operation,
            Path = "/test",
            Method = "GET",
        };

        return new OperationProcessorContext(
            new OpenApiDocument(),
            desc,
            typeof(object),
            typeof(object).GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            )!,
            null!,
            null!,
            null!,
            [desc]
        );
    }

    #endregion
}
