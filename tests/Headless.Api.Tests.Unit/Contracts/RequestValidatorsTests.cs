// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Headless.Api.Contracts;
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
