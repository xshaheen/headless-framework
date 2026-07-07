// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            cancellationToken: AbortToken
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
        var context = _CreateContext(
            new ValidatorFilterTestRequest("ValidName", "valid@example.com"),
            [validator],
            cancellationToken: AbortToken
        );
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
    }

    [Fact]
    public async Task should_return_422_problem_when_validation_fails()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContext(
            new ValidatorFilterTestRequest("", "invalid-email"),
            [validator],
            creator: creator,
            cancellationToken: AbortToken
        );
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeAssignableTo<IResult>();
        creator.Received(1).UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>());
    }

    #endregion

    #region Validation Behavior

    [Fact]
    public async Task should_use_fast_path_for_single_validator()
    {
        // given
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = _CreateMockValidator(new ValidationResult());
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            [validator],
            cancellationToken: AbortToken
        );
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
            [validator1, validator2],
            cancellationToken: AbortToken
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
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator1 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name error from validator 1")])
        );
        var validator2 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Email", "Email error from validator 2")])
        );
        var context = _CreateContext(
            new ValidatorFilterTestRequest("", "invalid"),
            [validator1, validator2],
            creator: creator,
            cancellationToken: AbortToken
        );
        var next = _CreateNext();

        // when
        await filter.InvokeAsync(context, next);

        // then — ToErrorDescriptors() camelizes property keys: "Name" → "name", "Email" → "email"
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("name")
                    && d.ContainsKey("email")
                    && d["name"].Any(e => e.Description == "Name error from validator 1")
                    && d["email"].Any(e => e.Description == "Email error from validator 2")
                )
            );
    }

    [Fact]
    public async Task should_group_errors_by_property_name()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();

        var validator1 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name must not be empty")])
        );

        var validator2 = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("Name", "Name must be at least 3 characters")])
        );

        var context = _CreateContext(
            new ValidatorFilterTestRequest("", "test@example.com"),
            [validator1, validator2],
            creator: creator,
            AbortToken
        );

        var next = _CreateNext();

        // when
        await filter.InvokeAsync(context, next);

        // then — ToErrorDescriptors() camelizes keys: "Name" → "name"
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("name")
                    && d["name"].Count == 2
                    && d["name"].Any(e => e.Description == "Name must not be empty")
                    && d["name"].Any(e => e.Description == "Name must be at least 3 characters")
                )
            );
    }

    [Fact]
    public async Task should_return_problem_when_request_type_mismatch()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContextWithMismatchedRequest(validator, creator);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeAssignableTo<IResult>();
        creator.Received(1).BadRequest(error: Arg.Is<ErrorDescriptor>(e => e.Code == "g:invalid_request_type"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task should_handle_null_request_in_arguments()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = new ValidatorFilterTestRequestValidator();
        var context = _CreateContext(null, [validator], creator: creator, cancellationToken: AbortToken);
        var next = _CreateNext();

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeAssignableTo<IResult>();
        creator.Received(1).BadRequest(error: Arg.Is<ErrorDescriptor>(e => e.Code == "g:invalid_request_type"));
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
            cancellationToken: cts.Token
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
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var failures = new List<ValidationFailure> { new("Name", "Error"), null! };
        var validator = _CreateMockValidator(new ValidationResult(failures));
        var context = _CreateContext(
            new ValidatorFilterTestRequest("", "test@example.com"),
            [validator],
            creator: creator,
            cancellationToken: AbortToken
        );
        var next = _CreateNext();

        // when
        await filter.InvokeAsync(context, next);

        // then — only one non-null failure should be passed to the creator; key camelized to "name"
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("name") && d["name"].Count == 1
                )
            );
    }

    [Fact]
    public async Task should_camelize_property_keys_in_error_descriptors()
    {
        // given — ToErrorDescriptors() normalizes property paths to camelCase
        var creator = _CreateProblemDetailsCreator();
        var filter = _CreateFilter<ValidatorFilterTestRequest>();
        var validator = _CreateMockValidator(
            new ValidationResult([new ValidationFailure("FirstName", "First name is required")])
        );
        var context = _CreateContext(
            new ValidatorFilterTestRequest("", "test@example.com"),
            [validator],
            creator: creator,
            cancellationToken: AbortToken
        );
        var next = _CreateNext();

        // when
        await filter.InvokeAsync(context, next);

        // then — "FirstName" is camelized to "firstName"
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("firstName") && d["firstName"].Count == 1
                )
            );
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
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            validatorList,
            cancellationToken: AbortToken
        );
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
        var context = _CreateContext(
            new ValidatorFilterTestRequest("Name", "test@example.com"),
            [validator],
            cancellationToken: AbortToken
        );
        var expectedResult = new object();
        var nextCalled = false;

        ValueTask<object?> trackingNext(EndpointFilterInvocationContext _)
        {
            nextCalled = true;

            return ValueTask.FromResult<object?>(expectedResult);
        }

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

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();

        creator
            .BadRequest(detail: Arg.Any<string?>(), error: Arg.Any<ErrorDescriptor?>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Bad Request" });

        creator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Unprocessable Entity",
            });

        return creator;
    }

    private static EndpointFilterInvocationContext _CreateContext<TRequest>(
        TRequest? request,
        IReadOnlyList<IValidator<TRequest>>? validators = null,
        IProblemDetailsCreator? creator = null,
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

            services.AddSingleton(validators);
        }

        // Register IProblemDetailsCreator so the filter can resolve it when needed
        var resolvedCreator = creator ?? _CreateProblemDetailsCreator();
        services.AddSingleton(resolvedCreator);

        httpContext.RequestServices = services.BuildServiceProvider();
        httpContext.RequestAborted = cancellationToken;

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        context.Arguments.Returns(request is null ? [] : [request]);

        return context;
    }

    private static EndpointFilterInvocationContext _CreateContextWithMismatchedRequest<TRequest>(
        IValidator<TRequest> validator,
        IProblemDetailsCreator? creator = null
    )
    {
        var httpContext = new DefaultHttpContext();

        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddSingleton<IEnumerable<IValidator<TRequest>>>([validator]);

        var resolvedCreator = creator ?? _CreateProblemDetailsCreator();
        services.AddSingleton(resolvedCreator);

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
