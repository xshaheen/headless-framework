// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Tests.Filters;

// Public to allow NSubstitute proxying with FluentValidation (strong-named assembly)
public record ValidatorFilterTestRequest(string Name, string Email);

public sealed class ValidatorFilterTestRequestValidator : AbstractValidator<ValidatorFilterTestRequest>
{
    public ValidatorFilterTestRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).EmailAddress();
    }
}

public sealed class MinimalApiValidatorFilterTests : TestBase
{
    #region Happy Path

    [Fact]
    public async Task should_call_next_when_no_validators_registered()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var context = _CreateContext<ValidatorFilterTestRequest>(
            new ValidatorFilterTestRequest("Name", "test@example.com")
        );
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
    }

    [Fact]
    public async Task should_call_next_when_validation_passes()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContext(new ValidatorFilterTestRequest("ValidName", "valid@example.com"), [validator]);
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
    }

    [Fact]
    public async Task should_return_validation_problem_when_fails()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContext(new ValidatorFilterTestRequest("", "invalid-email"), [validator]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        var (statusCode, errors) = await _ExecuteAndGetValidationErrors(result);
        statusCode.Should().Be(StatusCodes.Status400BadRequest);
        errors.Should().ContainKey("Name");
        errors.Should().ContainKey("Email");
    }

    #endregion

    #region Validation Behavior

    [Fact]
    public async Task should_use_fast_path_for_single_validator()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = _CreateMockValidator(new ValidationResult());
        var context = _CreateContext(new ValidatorFilterTestRequest("Name", "test@example.com"), [validator]);
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
        await validator
            .Received(1)
            .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_run_multiple_validators_in_parallel()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator1 = _CreateMockValidator(new ValidationResult());
        var validator2 = _CreateMockValidator(new ValidationResult());
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            [validator1, validator2]
        );
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
        await validator1
            .Received(1)
            .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>());
        await validator2
            .Received(1)
            .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_aggregate_errors_from_multiple_validators()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator1 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name error from validator 1")])
        );
        var validator2 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Email", "Email error from validator 2")])
        );
        var context = _CreateContext(new ValidatorFilterTestRequest("", "invalid"), [validator1, validator2]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        var (_, errors) = await _ExecuteAndGetValidationErrors(result);
        errors.Should().ContainKey("Name");
        errors.Should().ContainKey("Email");
        errors["Name"].Should().Contain("Name error from validator 1");
        errors["Email"].Should().Contain("Email error from validator 2");
    }

    [Fact]
    public async Task should_group_errors_by_property_name()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator1 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name must not be empty")])
        );
        var validator2 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name must be at least 3 characters")])
        );
        var context = _CreateContext(new ValidatorFilterTestRequest("", "test@example.com"), [validator1, validator2]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        var (_, errors) = await _ExecuteAndGetValidationErrors(result);
        errors.Should().ContainKey("Name");
        errors["Name"].Should().HaveCount(2);
        errors["Name"].Should().Contain("Name must not be empty");
        errors["Name"].Should().Contain("Name must be at least 3 characters");
    }

    [Fact]
    public async Task should_return_problem_when_request_type_mismatch()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContextWithMismatchedRequest(validator);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeAssignableTo<IResult>();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task should_handle_null_request_in_arguments()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContext<ValidatorFilterTestRequest>(null, [validator]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeAssignableTo<IResult>();
    }

    [Fact]
    public async Task should_pass_cancellation_token_to_validators()
    {
        // given
        using var cts = new CancellationTokenSource();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = _CreateMockValidator(new ValidationResult());
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            [validator],
            cts.Token
        );
        var next = _CreateNext(new object());

        // when
        await filter.InvokeAsync(context, next);

        // then
        await validator.Received(1).ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), cts.Token);
    }

    [Fact]
    public async Task should_filter_null_validation_failures()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var failures = new List<ValidationFailure> { new("Name", "Error"), null! };
        var validator = _CreateMockValidator(new ValidationResult(failures));
        var context = _CreateContext(new ValidatorFilterTestRequest("", "test@example.com"), [validator]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        var (_, errors) = await _ExecuteAndGetValidationErrors(result);
        errors.Should().ContainKey("Name");
        errors["Name"].Should().HaveCount(1);
    }

    [Fact]
    public async Task should_use_ordinal_string_comparison_for_grouping()
    {
        // given - test case sensitivity with ordinal comparison
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator1 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("name", "lowercase name error")])
        );
        var validator2 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "uppercase Name error")])
        );
        var context = _CreateContext(new ValidatorFilterTestRequest("", "test@example.com"), [validator1, validator2]);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then - ordinal comparison means "name" and "Name" are different keys
        var (_, errors) = await _ExecuteAndGetValidationErrors(result);
        errors.Should().ContainKey("name");
        errors.Should().ContainKey("Name");
        errors["name"].Should().HaveCount(1);
        errors["Name"].Should().HaveCount(1);
    }

    #endregion

    #region Performance

    [Fact]
    public async Task should_not_allocate_list_when_validators_is_list()
    {
        // given - when validators is already a list, it should use it directly
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validatorList = new List<IValidator<ValidatorFilterTestRequest>>
        {
            _CreateMockValidator(new ValidationResult()),
            _CreateMockValidator(new ValidationResult()),
        };
        var context = _CreateContext(new ValidatorFilterTestRequest("Name", "test@example.com"), validatorList);
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
        foreach (var v in validatorList)
        {
            await v.Received(1)
                .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task should_exit_early_when_all_valid()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = _CreateMockValidator(new ValidationResult()); // IsValid = true
        var context = _CreateContext(new ValidatorFilterTestRequest("Name", "test@example.com"), [validator]);
        var expectedResult = new object();
        var nextCalled = false;
        EndpointFilterDelegate trackingNext = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(expectedResult);
        };

        // when
        var result = await filter.InvokeAsync(context, trackingNext);

        // then
        result.Should().BeSameAs(expectedResult);
        nextCalled.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static MinimalApiValidatorFilter<TRequest> _CreateFilter<TRequest>() => new();

    private static IValidator<ValidatorFilterTestRequest> _CreateMockValidator(ValidationResult result)
    {
        var validator = Substitute.For<IValidator<ValidatorFilterTestRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return validator;
    }

    private static async Task<(int StatusCode, Dictionary<string, string[]> Errors)> _ExecuteAndGetValidationErrors(
        object? result
    )
    {
        result.Should().NotBeNull();
        var httpResult = result as IResult;
        httpResult.Should().NotBeNull();

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Response.Body = new MemoryStream();

        await httpResult!.ExecuteAsync(httpContext);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();

        var problemDetails = JsonSerializer.Deserialize<JsonElement>(body);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (problemDetails.TryGetProperty("errors", out var errorsElement))
        {
            foreach (var prop in errorsElement.EnumerateObject())
            {
                var values = prop.Value.EnumerateArray().Select(v => v.GetString()!).ToArray();
                errors[prop.Name] = values;
            }
        }

        return (httpContext.Response.StatusCode, errors);
    }

    private static EndpointFilterInvocationContext _CreateContext<TRequest>(
        TRequest? request,
        IEnumerable<IValidator<TRequest>>? validators = null,
        CancellationToken cancellationToken = default
    )
    {
        var httpContext = new DefaultHttpContext();

        var services = new ServiceCollection();
        if (validators is not null)
        {
            foreach (var v in validators)
            {
                services.AddSingleton(v);
            }

            services.AddSingleton<IEnumerable<IValidator<TRequest>>>(validators);
        }

        httpContext.RequestServices = services.BuildServiceProvider();
        httpContext.RequestAborted = cancellationToken;

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        context.Arguments.Returns(request is null ? [] : [request]);

        return context;
    }

    private static EndpointFilterInvocationContext _CreateContextWithMismatchedRequest<TRequest>(
        IValidator<TRequest> validator
    )
    {
        var httpContext = new DefaultHttpContext();

        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddSingleton<IEnumerable<IValidator<TRequest>>>([validator]);
        httpContext.RequestServices = services.BuildServiceProvider();

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        // Argument is a different type (string instead of TRequest)
        context.Arguments.Returns(new object[] { "wrong type" });

        return context;
    }

    private static EndpointFilterDelegate _CreateNext(object? result = null)
    {
        return _ => ValueTask.FromResult(result);
    }

    #endregion
}
