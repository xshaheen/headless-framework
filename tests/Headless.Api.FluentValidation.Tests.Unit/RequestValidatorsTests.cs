// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Headless.Api;
using Headless.Primitives;
using Headless.Testing.Tests;
using Headless.Validators;

namespace Tests;

public sealed class RequestValidatorsTests : TestBase
{
    [Fact]
    public void should_skip_null_values_when_phone_number_request_validator()
    {
        PhoneNumberModelValidator validator = new();

        var result = validator.TestValidate(new PhoneNumberModel { PhoneNumber = null });

        result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void should_reject_invalid_values_when_phone_number_request_validator()
    {
        PhoneNumberModelValidator validator = new();

        var result = validator.TestValidate(new PhoneNumberModel { PhoneNumber = new PhoneNumberRequest(0, "") });

        result.ShouldHaveValidationErrorFor("PhoneNumber.Code");
        result.ShouldHaveValidationErrorFor("PhoneNumber.Number");
    }

    [Fact]
    public void should_skip_null_values_when_geo_coordinate_request_validator()
    {
        GeoCoordinateModelValidator validator = new();

        var result = validator.TestValidate(new GeoCoordinateModel { Coordinate = null });

        result.ShouldNotHaveValidationErrorFor(x => x.Coordinate);
    }

    [Fact]
    public void should_reject_out_of_range_values_when_geo_coordinate_request_validator()
    {
        GeoCoordinateModelValidator validator = new();

        var result = validator.TestValidate(
            new GeoCoordinateModel
            {
                Coordinate = new GeoCoordinateRequest(GeoCoordinateValidator.LatitudeMaxValue + 1, 0),
            }
        );

        result.ShouldHaveValidationErrorFor(x => x.Coordinate);
    }

    [Fact]
    public void should_skip_null_values_when_page_metadata_request_validator()
    {
        PageMetadataModelValidator validator = new();

        var result = validator.TestValidate(new PageMetadataModel { Metadata = null });

        result.ShouldNotHaveValidationErrorFor(x => x.Metadata);
    }

    [Fact]
    public void should_reject_values_over_limits_when_page_metadata_request_validator()
    {
        PageMetadataModelValidator validator = new();

        var result = validator.TestValidate(
            new PageMetadataModel
            {
                Metadata = new PageMetadataRequest
                {
                    Slug = new string('a', PageMetadataConstants.Slugs.MaxLength + 1),
                    MetaTitle = "Title",
                    MetaDescription = "Description",
                },
            }
        );

        result.ShouldHaveValidationErrorFor("Metadata.Slug");
    }

    [Fact]
    public void should_skip_null_values_when_index_page_request_validator()
    {
        IndexPageModelValidator validator = new();

        var result = validator.TestValidate(new IndexPageModel { Page = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 500)]
    public void should_accept_a_valid_page_when_index_page_request_validator(int index, int size)
    {
        IndexPageModelValidator validator = new();

        var result = validator.TestValidate(
            new IndexPageModel
            {
                Page = new() { Index = index, Size = size },
            }
        );

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_size_below_one_when_index_page_request_validator(int size)
    {
        IndexPageModelValidator validator = new();

        var result = validator.TestValidate(
            new IndexPageModel
            {
                Page = new() { Index = 0, Size = size },
            }
        );

        result.ShouldHaveValidationErrorFor("Page.Size");
        result.ShouldNotHaveValidationErrorFor("Page.Index");
    }

    [Fact]
    public void should_reject_a_negative_index_when_index_page_request_validator()
    {
        IndexPageModelValidator validator = new();

        var result = validator.TestValidate(
            new IndexPageModel
            {
                Page = new() { Index = -1, Size = 5 },
            }
        );

        result.ShouldHaveValidationErrorFor("Page.Index");
        result.ShouldNotHaveValidationErrorFor("Page.Size");
    }

    [Fact]
    public void should_bind_to_a_non_nullable_page_when_index_page_request_validator()
    {
        RequiredIndexPageModelValidator validator = new();

        var result = validator.TestValidate(
            new RequiredIndexPageModel
            {
                Page = new() { Index = 0, Size = 0 },
            }
        );

        result.ShouldHaveValidationErrorFor("Page.Size");
    }

    private sealed class PhoneNumberModel
    {
        public PhoneNumberRequest? PhoneNumber { get; init; }
    }

    private sealed class PhoneNumberModelValidator : AbstractValidator<PhoneNumberModel>
    {
        public PhoneNumberModelValidator()
        {
            RuleFor(x => x.PhoneNumber).PhoneNumber();
        }
    }

    private sealed class GeoCoordinateModel
    {
        public GeoCoordinateRequest? Coordinate { get; init; }
    }

    private sealed class GeoCoordinateModelValidator : AbstractValidator<GeoCoordinateModel>
    {
        public GeoCoordinateModelValidator()
        {
            RuleFor(x => x.Coordinate).GeoCoordinate();
        }
    }

    private sealed class IndexPageModel
    {
        public IndexPageRequest? Page { get; init; }
    }

    private sealed class IndexPageModelValidator : AbstractValidator<IndexPageModel>
    {
        public IndexPageModelValidator()
        {
            RuleFor(x => x.Page).IndexPage();
        }
    }

    private sealed class RequiredIndexPageModel
    {
        public required IndexPageRequest Page { get; init; }
    }

    private sealed class RequiredIndexPageModelValidator : AbstractValidator<RequiredIndexPageModel>
    {
        public RequiredIndexPageModelValidator()
        {
            RuleFor(x => x.Page).IndexPage();
        }
    }

    private sealed class PageMetadataModel
    {
        public PageMetadataRequest? Metadata { get; init; }
    }

    private sealed class PageMetadataModelValidator : AbstractValidator<PageMetadataModel>
    {
        public PageMetadataModelValidator()
        {
            RuleFor(x => x.Metadata).PageMetadata();
        }
    }
}
