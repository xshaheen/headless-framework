// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

#region Latitude (double)

public sealed class GeoValidatorsDoubleLatitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(double Latitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Latitude).Latitude();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45.5)]
    [InlineData(-45.5)]
    public void should_not_have_error_when_latitude_in_range(double latitude)
    {
        var model = new TestModel(latitude);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_have_error_when_latitude_below_min()
    {
        var model = new TestModel(-91);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_have_error_when_latitude_above_max()
    {
        var model = new TestModel(91);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(90)]
    public void should_not_have_error_when_latitude_at_boundary(double latitude)
    {
        var model = new TestModel(latitude);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }
}

#endregion

#region Latitude (double?)

public sealed class GeoValidatorsNullableDoubleLatitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(double? Latitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Latitude).Latitude();
    }

    [Fact]
    public void should_not_have_error_when_nullable_latitude_is_null()
    {
        var model = new TestModel(Latitude: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_not_have_error_when_nullable_latitude_valid()
    {
        var model = new TestModel(45.0);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_have_error_when_nullable_latitude_invalid()
    {
        var model = new TestModel(100.0);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }
}

#endregion

#region Latitude (string)

public sealed class GeoValidatorsStringLatitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? Latitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Latitude!).Latitude();
    }

    [Fact]
    public void should_not_have_error_when_string_latitude_is_null()
    {
        var model = new TestModel(Latitude: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_not_have_error_when_string_latitude_valid()
    {
        var model = new TestModel("45.5");
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_have_error_when_string_latitude_not_number()
    {
        var model = new TestModel("abc");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void should_have_error_when_string_latitude_out_of_range()
    {
        var model = new TestModel("100");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }
}

#endregion

#region Longitude (double)

public sealed class GeoValidatorsDoubleLongitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(double Longitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Longitude).Longitude();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90.5)]
    [InlineData(-90.5)]
    public void should_not_have_error_when_longitude_in_range(double longitude)
    {
        var model = new TestModel(longitude);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_have_error_when_longitude_below_min()
    {
        var model = new TestModel(-181);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_have_error_when_longitude_above_max()
    {
        var model = new TestModel(181);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Theory]
    [InlineData(-180)]
    [InlineData(180)]
    public void should_not_have_error_when_longitude_at_boundary(double longitude)
    {
        var model = new TestModel(longitude);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }
}

#endregion

#region Longitude (double?)

public sealed class GeoValidatorsNullableDoubleLongitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(double? Longitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Longitude).Longitude();
    }

    [Fact]
    public void should_not_have_error_when_nullable_longitude_is_null()
    {
        var model = new TestModel(Longitude: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_not_have_error_when_nullable_longitude_valid()
    {
        var model = new TestModel(90.0);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_have_error_when_nullable_longitude_invalid()
    {
        var model = new TestModel(200.0);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }
}

#endregion

#region Longitude (string)

public sealed class GeoValidatorsStringLongitudeTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? Longitude);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Longitude!).Longitude();
    }

    [Fact]
    public void should_not_have_error_when_string_longitude_is_null()
    {
        var model = new TestModel(Longitude: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_not_have_error_when_string_longitude_valid()
    {
        var model = new TestModel("90.5");
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_have_error_when_string_longitude_not_number()
    {
        var model = new TestModel("abc");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void should_have_error_when_string_longitude_out_of_range()
    {
        var model = new TestModel("200");
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }
}

#endregion
