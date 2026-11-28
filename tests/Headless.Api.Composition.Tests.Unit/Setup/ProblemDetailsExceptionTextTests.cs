// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class ProblemDetailsExceptionTextTests : TestBase
{
    private const string _NationalId = "29001011234567";

    [Fact]
    public void should_mask_every_string_of_a_body_written_for_an_exception()
    {
        // given: the shape the developer exception page writes for a JSON client
        using var provider = _Build(withRedaction: true);
        using var document = JsonDocument.Parse(
            $$"""
            {
              "details": "System.InvalidOperationException: no citizen {{_NationalId}}\n   at Lookup.Find()",
              "headers": { "X-Citizen": ["{{_NationalId}}"] },
              "routeValues": { "id": "{{_NationalId}}" },
              "attempt": 3
            }
            """
        );
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Detail = $"no citizen {_NationalId}",
            Extensions = { ["exception"] = document.RootElement.Clone(), ["note"] = $"citizen {_NationalId}" },
        };

        // when
        _Customize(provider, problemDetails, new InvalidOperationException($"no citizen {_NationalId}"));

        // then
        problemDetails.Detail.Should().Be("no citizen ***");
        problemDetails.Extensions["note"].Should().Be("citizen ***");
        var exception = problemDetails.Extensions["exception"].Should().BeOfType<JsonElement>().Subject;
        exception.GetRawText().Should().NotContain(_NationalId);
        exception
            .GetProperty("details")
            .GetString()
            .Should()
            .StartWith("System.InvalidOperationException: no citizen ***");
        exception.GetProperty("headers").GetProperty("X-Citizen")[0].GetString().Should().Be("***");
        exception.GetProperty("routeValues").GetProperty("id").GetString().Should().Be("***");
        exception.GetProperty("attempt").GetInt32().Should().Be(3);
    }

    [Fact]
    public void should_keep_a_body_the_app_wrote_without_an_exception()
    {
        // given
        using var provider = _Build(withRedaction: true);
        var problemDetails = new ProblemDetails { Detail = $"citizen {_NationalId} is already registered" };

        // when
        _Customize(provider, problemDetails, exception: null);

        // then
        problemDetails.Detail.Should().Be($"citizen {_NationalId} is already registered");
    }

    [Fact]
    public void should_keep_an_exception_body_when_no_text_masker_is_registered()
    {
        // given
        using var provider = _Build(withRedaction: false);
        var problemDetails = new ProblemDetails { Detail = $"no citizen {_NationalId}" };

        // when
        _Customize(provider, problemDetails, new InvalidOperationException($"no citizen {_NationalId}"));

        // then
        problemDetails.Detail.Should().Be($"no citizen {_NationalId}");
    }

    private static void _Customize(ServiceProvider provider, ProblemDetails problemDetails, Exception? exception)
    {
        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        var context = new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = provider },
            ProblemDetails = problemDetails,
            Exception = exception,
        };

        options.CustomizeProblemDetails!(context);
    }

    private static ServiceProvider _Build(bool withRedaction)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<Headless.Context.IBuildInformationAccessor>());
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessProblemDetails();

        if (withRedaction)
        {
            services.AddHeadlessRedaction(static setup => setup.MaskText(@"\b\d{14}\b"));
        }

        return services.BuildServiceProvider();
    }
}
