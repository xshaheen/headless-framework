// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class PaginationValidatorsTests
{
    #region PageIndex (int?) Tests

    private sealed class NullablePageIndexModel
    {
        public int? PageIndex { get; init; }
    }

    private sealed class NullablePageIndexValidator : AbstractValidator<NullablePageIndexModel>
    {
        public NullablePageIndexValidator()
        {
            RuleFor(x => x.PageIndex).PageIndex();
        }
    }

    [Fact]
    public void should_not_have_error_when_nullable_page_index_is_null()
    {
        var sut = new NullablePageIndexValidator();
        var model = new NullablePageIndexModel { PageIndex = null };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageIndex);
    }

    [Fact]
    public void should_not_have_error_when_nullable_page_index_is_zero()
    {
        var sut = new NullablePageIndexValidator();
        var model = new NullablePageIndexModel { PageIndex = 0 };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageIndex);
    }

    [Fact]
    public void should_not_have_error_when_nullable_page_index_is_positive()
    {
        var sut = new NullablePageIndexValidator();
        var model = new NullablePageIndexModel { PageIndex = 5 };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageIndex);
    }

    [Fact]
    public void should_have_error_when_nullable_page_index_is_negative()
    {
        var sut = new NullablePageIndexValidator();
        var model = new NullablePageIndexModel { PageIndex = -1 };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageIndex);
    }

    #endregion

    #region PageIndex (int) Tests

    private sealed class PageIndexModel
    {
        public int PageIndex { get; init; }
    }

    private sealed class PageIndexValidator : AbstractValidator<PageIndexModel>
    {
        public PageIndexValidator()
        {
            RuleFor(x => x.PageIndex).PageIndex();
        }
    }

    [Fact]
    public void should_not_have_error_when_page_index_is_zero()
    {
        var sut = new PageIndexValidator();
        var model = new PageIndexModel { PageIndex = 0 };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageIndex);
    }

    [Fact]
    public void should_not_have_error_when_page_index_is_positive()
    {
        var sut = new PageIndexValidator();
        var model = new PageIndexModel { PageIndex = 10 };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageIndex);
    }

    [Fact]
    public void should_have_error_when_page_index_is_negative()
    {
        var sut = new PageIndexValidator();
        var model = new PageIndexModel { PageIndex = -1 };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageIndex);
    }

    #endregion

    #region PageSize (int) Tests

    private sealed class PageSizeModel
    {
        public int PageSize { get; init; }
    }

    private sealed class PageSizeValidator : AbstractValidator<PageSizeModel>
    {
        public PageSizeValidator()
        {
            RuleFor(x => x.PageSize).PageSize();
        }
    }

    private sealed class PageSizeCustomMaxValidator : AbstractValidator<PageSizeModel>
    {
        public PageSizeCustomMaxValidator(int maximumSize)
        {
            RuleFor(x => x.PageSize).PageSize(maximumSize);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void should_not_have_error_when_page_size_is_valid(int pageSize)
    {
        var sut = new PageSizeValidator();
        var model = new PageSizeModel { PageSize = pageSize };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void should_have_error_when_page_size_is_zero()
    {
        var sut = new PageSizeValidator();
        var model = new PageSizeModel { PageSize = 0 };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void should_have_error_when_page_size_exceeds_max()
    {
        var sut = new PageSizeValidator();
        var model = new PageSizeModel { PageSize = 101 };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void should_use_custom_max_size()
    {
        var sut = new PageSizeCustomMaxValidator(50);

        // Valid at custom max
        var validModel = new PageSizeModel { PageSize = 50 };
        var validResult = sut.TestValidate(validModel);
        validResult.ShouldNotHaveValidationErrorFor(x => x.PageSize);

        // Invalid above custom max
        var invalidModel = new PageSizeModel { PageSize = 51 };
        var invalidResult = sut.TestValidate(invalidModel);
        invalidResult.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    #endregion

    #region SearchQuery (string?) Tests

    private sealed class SearchQueryModel
    {
        public string? SearchQuery { get; init; }
    }

    private sealed class SearchQueryValidator : AbstractValidator<SearchQueryModel>
    {
        public SearchQueryValidator()
        {
            RuleFor(x => x.SearchQuery).SearchQuery();
        }
    }

    private sealed class SearchQueryCustomMaxValidator : AbstractValidator<SearchQueryModel>
    {
        public SearchQueryCustomMaxValidator(int maximumLength)
        {
            RuleFor(x => x.SearchQuery).SearchQuery(maximumLength);
        }
    }

    [Fact]
    public void should_not_have_error_when_search_query_is_null()
    {
        var sut = new SearchQueryValidator();
        var model = new SearchQueryModel { SearchQuery = null };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.SearchQuery);
    }

    [Fact]
    public void should_not_have_error_when_search_query_within_limit()
    {
        var sut = new SearchQueryValidator();
        var model = new SearchQueryModel { SearchQuery = new string('a', 100) };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.SearchQuery);
    }

    [Fact]
    public void should_have_error_when_search_query_exceeds_max()
    {
        var sut = new SearchQueryValidator();
        var model = new SearchQueryModel { SearchQuery = new string('a', 101) };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.SearchQuery);
    }

    [Fact]
    public void should_use_custom_max_length()
    {
        var sut = new SearchQueryCustomMaxValidator(50);

        // Valid at custom max
        var validModel = new SearchQueryModel { SearchQuery = new string('a', 50) };
        var validResult = sut.TestValidate(validModel);
        validResult.ShouldNotHaveValidationErrorFor(x => x.SearchQuery);

        // Invalid above custom max
        var invalidModel = new SearchQueryModel { SearchQuery = new string('a', 51) };
        var invalidResult = sut.TestValidate(invalidModel);
        invalidResult.ShouldHaveValidationErrorFor(x => x.SearchQuery);
    }

    #endregion
}
