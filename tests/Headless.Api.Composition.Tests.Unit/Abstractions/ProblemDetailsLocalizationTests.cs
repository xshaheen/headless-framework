// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Resources;
using Headless.Context;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Abstractions;

public sealed class ProblemDetailsLocalizationTests : TestBase
{
    private const string _RequestPath = "/api/orders";

    private static readonly CultureInfo _English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo _Arabic = CultureInfo.GetCultureInfo("ar");

    // Each case builds a response through the public factory surface, or through Normalize for the
    // statuses that only Normalize fills, and names the resource its default detail comes from.
    private static readonly Dictionary<
        string,
        (Func<ProblemDetailsCreator, ProblemDetails> Create, string Key)
    > _Cases = new(StringComparer.Ordinal)
    {
        ["endpoint-not-found"] = (c => c.EndpointNotFound(), "problem:endpoint_not_found"),
        ["entity-not-found"] = (c => c.EntityNotFound(), "problem:entity_not_found"),
        ["bad-request"] = (c => c.BadRequest(), "problem:bad_request"),
        ["unprocessable-entity"] = (
            c => c.UnprocessableEntity(new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)),
            "problem:unprocessable_entity"
        ),
        ["conflict"] = (c => c.Conflict(new ErrorDescriptor("app:conflict", "Conflict.")), "problem:conflict"),
        ["forbidden"] = (c => c.Forbidden(), "problem:forbidden"),
        ["unauthorized"] = (c => c.Unauthorized(), "problem:unauthorized"),
        ["request-timeout"] = (c => c.RequestTimeout(), "problem:request_timeout"),
        ["not-implemented"] = (c => c.NotImplemented(), "problem:not_implemented"),
        ["too-many-requests"] = (c => c.TooManyRequests(retryAfterSeconds: 30), "problem:too_many_requests"),
        ["service-unavailable"] = (c => c.ServiceUnavailable(), "problem:service_unavailable"),
        ["normalize-500"] = (c => _Normalized(c, StatusCodes.Status500InternalServerError), "problem:internal_error"),
        ["normalize-404"] = (c => _Normalized(c, StatusCodes.Status404NotFound), "problem:endpoint_not_found"),
        ["normalize-408"] = (c => _Normalized(c, StatusCodes.Status408RequestTimeout), "problem:request_timeout"),
        ["normalize-413"] = (c => _Normalized(c, StatusCodes.Status413PayloadTooLarge), "problem:payload_too_large"),
        ["normalize-501"] = (c => _Normalized(c, StatusCodes.Status501NotImplemented), "problem:not_implemented"),
    };

    public static TheoryData<string> Cases => [.. _Cases.Keys];

    // Factories that accept a caller-supplied detail.
    private static readonly Dictionary<string, Func<ProblemDetailsCreator, string, ProblemDetails>> _DetailFactories =
        new(StringComparer.Ordinal)
        {
            ["endpoint-not-found"] = (c, d) => c.EndpointNotFound(d),
            ["entity-not-found"] = (c, d) => c.EntityNotFound(d),
            ["bad-request"] = (c, d) => c.BadRequest(d),
            ["forbidden"] = (c, d) => c.Forbidden(d),
            ["unauthorized"] = (c, d) => c.Unauthorized(d),
            ["request-timeout"] = (c, d) => c.RequestTimeout(d),
            ["not-implemented"] = (c, d) => c.NotImplemented(d),
            ["too-many-requests"] = (c, d) => c.TooManyRequests(30, d),
            ["service-unavailable"] = (c, d) => c.ServiceUnavailable(detail: d),
        };

    public static TheoryData<string> DetailFactories => [.. _DetailFactories.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void should_resolve_default_detail_under_current_ui_culture(string caseName)
    {
        // given
        var (create, key) = _Cases[caseName];
        var creator = _CreateCreator();

        // when
        var english = _InCulture(_English, () => create(creator));
        var arabic = _InCulture(_Arabic, () => create(creator));

        // then
        english.Detail.Should().Be(_Expected(key, _English));
        arabic.Detail.Should().Be(_Expected(key, _Arabic));
        arabic.Detail.Should().NotBe(english.Detail, "an Arabic translation must ship for every default detail");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void should_keep_type_status_and_title_invariant_across_cultures(string caseName)
    {
        // given
        var (create, _) = _Cases[caseName];
        var creator = _CreateCreator();

        // when
        var english = _InCulture(_English, () => create(creator));
        var arabic = _InCulture(_Arabic, () => create(creator));

        // then
        arabic.Type.Should().Be(english.Type);
        arabic.Status.Should().Be(english.Status);
        arabic.Title.Should().Be(english.Title);
        arabic.Title.Should().MatchRegex("^[a-z]+(-[a-z]+)*$", "titles are kebab-case identifiers, not prose");
    }

    [Theory]
    [MemberData(nameof(DetailFactories))]
    public void should_pass_caller_detail_through_untouched(string caseName)
    {
        // given
        const string callerDetail = "Order 42 is locked by another checkout.";
        var factory = _DetailFactories[caseName];
        var creator = _CreateCreator();

        // when
        var result = _InCulture(_Arabic, () => factory(creator, callerDetail));

        // then
        result.Detail.Should().Be(callerDetail);
    }

    [Fact]
    public void should_not_overwrite_caller_detail_when_normalizing()
    {
        // given
        const string callerDetail = "The ledger is being rebuilt.";
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Detail = callerDetail,
        };

        // when
        _InCulture(_Arabic, () => creator.Normalize(problemDetails));

        // then
        problemDetails.Detail.Should().Be(callerDetail);
    }

    [Fact]
    public void should_keep_error_code_invariant_across_cultures()
    {
        // given
        var creator = _CreateCreator();

        // when
        var english = _InCulture(_English, () => creator.Forbidden(error: GeneralMessageDescriber.TenantRequired()));
        var arabic = _InCulture(_Arabic, () => creator.Forbidden(error: GeneralMessageDescriber.TenantRequired()));

        // then
        var englishError = english.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Subject;
        var arabicError = arabic.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Subject;
        englishError.Code.Should().Be(GeneralErrorCodes.TenantRequired);
        arabicError.Code.Should().Be(GeneralErrorCodes.TenantRequired);
        arabicError.Description.Should().NotBe(englishError.Description);
    }

    [Theory]
    [InlineData("tenant-required")]
    [InlineData("cross-tenant-write")]
    [InlineData("invalid-request-type")]
    public void should_resolve_framework_descriptor_under_the_current_culture_not_the_first_access_culture(
        string descriptorName
    )
    {
        // given
        Func<ErrorDescriptor> describe = descriptorName switch
        {
            "tenant-required" => GeneralMessageDescriber.TenantRequired,
            "cross-tenant-write" => GeneralMessageDescriber.CrossTenantWrite,
            _ => GeneralMessageDescriber.InvalidRequestType,
        };

        // when: the first access runs under English, the second under Arabic
        var first = _InCulture(_English, describe);
        var second = _InCulture(_Arabic, describe);

        // then
        second.Code.Should().Be(first.Code);
        first.Description.Should().Be(_Expected(first.Code, _English));
        second.Description.Should().Be(_Expected(first.Code, _Arabic));
        second.Description.Should().NotBe(first.Description);
    }

    [Fact]
    public void should_apply_localizer_to_single_error_and_keep_code_severity_and_params()
    {
        // given
        var creator = _CreateCreator(new DictionaryLocalizer(("app:out_of_stock", "نفدت الكمية")));
        var error = new ErrorDescriptor(
            "app:out_of_stock",
            "Out of stock.",
            ValidationSeverity.Warning,
            ("sku", "A-1")
        );

        // when
        var result = creator.Conflict(error);
        var single = creator.BadRequest(error: error);

        // then
        var localized = result
            .Extensions["errors"]
            .Should()
            .BeAssignableTo<IReadOnlyCollection<ErrorDescriptor>>()
            .Subject.Should()
            .ContainSingle()
            .Subject;
        localized.Code.Should().Be("app:out_of_stock");
        localized.Description.Should().Be("نفدت الكمية");
        localized.Severity.Should().Be(ValidationSeverity.Warning);
        localized.Params.Should().ContainKey("sku").WhoseValue.Should().Be("A-1");

        single.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Which.Description.Should().Be("نفدت الكمية");

        // the caller's descriptor is copied, never mutated
        error.Description.Should().Be("Out of stock.");
    }

    [Fact]
    public void should_apply_localizer_to_field_errors_and_keep_unlocalized_fields()
    {
        // given
        var creator = _CreateCreator(new DictionaryLocalizer(("app:email_taken", "البريد مستخدم")));
        var errors = new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["name"] = [new ErrorDescriptor("app:name_required", "Name is required.")],
            ["email"] = [new ErrorDescriptor("app:email_taken", "Email is taken.")],
        };

        // when
        var result = creator.UnprocessableEntity(errors);

        // then
        var written = result
            .Extensions["errors"]
            .Should()
            .BeAssignableTo<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>()
            .Subject;
        written.Keys.Should().Equal("name", "email");
        written["name"].Should().ContainSingle().Which.Description.Should().Be("Name is required.");
        written["email"].Should().ContainSingle().Which.Description.Should().Be("البريد مستخدم");
    }

    [Fact]
    public void should_keep_original_descriptor_when_localizer_returns_null()
    {
        // given
        var creator = _CreateCreator(new DictionaryLocalizer());
        var error = new ErrorDescriptor("app:unknown_to_localizer", "Original description.");
        IReadOnlyCollection<ErrorDescriptor> errors = [error];

        // when
        var single = creator.Forbidden(error: error);
        var many = creator.Conflict(errors);

        // then
        single.Extensions["error"].Should().BeSameAs(error);
        many.Extensions["errors"].Should().BeSameAs(errors);
    }

    [Fact]
    public void should_apply_localizer_to_descriptors_on_externally_built_problem_details()
    {
        // given
        var creator = _CreateCreator(new DictionaryLocalizer(("app:locked", "مقفل")));
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status423Locked,
            Extensions = { ["error"] = new ErrorDescriptor("app:locked", "Locked.") },
        };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Which.Description.Should().Be("مقفل");
    }

    private static ProblemDetails _Normalized(ProblemDetailsCreator creator, int status)
    {
        var problemDetails = new ProblemDetails { Status = status };
        creator.Normalize(problemDetails);

        return problemDetails;
    }

    private static string _Expected(string key, CultureInfo culture)
    {
        var value = Messages.ResourceManager.GetString(key, culture);
        value.Should().NotBeNull($"resource '{key}' must exist");

        return string.Format(CultureInfo.InvariantCulture, value!, _RequestPath);
    }

    private static T _InCulture<T>(CultureInfo culture, Func<T> action)
    {
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = culture;

            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static void _InCulture(CultureInfo culture, Action action)
    {
        _InCulture(
            culture,
            () =>
            {
                action();
                return 0;
            }
        );
    }

    private static ProblemDetailsCreator _CreateCreator(IErrorDescriptionLocalizer? localizer = null)
    {
        var buildInfo = Substitute.For<IBuildInformationAccessor>();
        buildInfo.GetVersion().Returns("1.0.0");
        buildInfo.GetCommitNumber().Returns("abc123");

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext { Request = { Path = _RequestPath } });

        return new ProblemDetailsCreator(
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            buildInfo,
            httpContextAccessor,
            Options.Create(new ApiBehaviorOptions()),
            localizer ?? NullErrorDescriptionLocalizer.Instance
        );
    }

    private sealed class DictionaryLocalizer(params (string Code, string Description)[] entries)
        : IErrorDescriptionLocalizer
    {
        private readonly Dictionary<string, string> _entries = entries.ToDictionary(
            e => e.Code,
            e => e.Description,
            StringComparer.Ordinal
        );

        public string? Localize(ErrorDescriptor error) => _entries.GetValueOrDefault(error.Code);
    }
}
