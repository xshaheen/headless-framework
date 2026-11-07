// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Json.Nodes;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Aggregation;

/// <summary>A payload passes through its module's formatter before it leaves the server.</summary>
public sealed class PayloadFormattingTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity());

    private static readonly DashboardPayload _Raw = new()
    {
        Content = """{"user":"ada","password":"hunter2"}""",
        ContentType = "application/json",
    };

    [Fact]
    public async Task should_return_the_payload_masked_when_the_module_formatter_masks_password()
    {
        var module = new DiagAModule { ObjectKinds = ["run"], Format = _MaskPassword };
        await using var provider = _Build(module);
        var formatting = provider.GetRequiredService<DashboardPayloadFormatting>();

        var payload = await formatting.FormatAsync(_Run("diag-a", "run"), _Raw, _User, AbortToken);

        var body = JsonNode.Parse(payload.Content)!;
        body["password"]!.GetValue<string>().Should().Be("***");
        body["user"]!.GetValue<string>().Should().Be("ada");
    }

    [Fact]
    public async Task should_pass_the_json_through_when_the_formatter_does_not_handle_the_object_kind()
    {
        var module = new DiagAModule { ObjectKinds = ["message"], Format = _MaskPassword };
        await using var provider = _Build(module);
        var formatting = provider.GetRequiredService<DashboardPayloadFormatting>();

        var payload = await formatting.FormatAsync(_Run("diag-a", "run"), _Raw, _User, AbortToken);

        payload.Should().BeSameAs(_Raw);
    }

    [Fact]
    public async Task should_pass_the_json_through_when_no_registered_module_owns_the_object()
    {
        await using var provider = _Build(new DiagAModule { ObjectKinds = ["run"], Format = _MaskPassword });
        var formatting = provider.GetRequiredService<DashboardPayloadFormatting>();

        var payload = await formatting.FormatAsync(_Run("other", "run"), _Raw, _User, AbortToken);

        payload.Should().BeSameAs(_Raw);
    }

    [Fact]
    public async Task should_refuse_an_aggregation_timeout_that_is_not_positive()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth());
        services.Configure<HeadlessDashboardOptions>(options => options.AggregationTimeout = TimeSpan.Zero);
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*AggregationTimeout*");
    }

    private static ServiceProvider _Build(DiagAModule module)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(module);
        services.AddHeadlessDashboard(dashboard =>
            dashboard.WithNoAuth().AddModule<DiagAModule, DiagAModuleOptions>("diag-a")
        );

        return services.BuildServiceProvider();
    }

    private static DashboardPayload _MaskPassword(DashboardPayload payload)
    {
        var body = JsonNode.Parse(payload.Content)!.AsObject();

        if (body.ContainsKey("password"))
        {
            body["password"] = "***";
        }

        return payload with
        {
            Content = body.ToJsonString(),
        };
    }

    private static ObjectRef _Run(string module, string kind)
    {
        return new ObjectRef
        {
            Module = module,
            Kind = kind,
            Id = "run-42",
        };
    }
}
