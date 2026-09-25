// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Features.ClientConfig;
using Headless.Features.Definitions;
using Headless.Features.Models;
using Headless.Features.Values;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.ClientConfig;

public sealed class ClientFeaturesConfigBuilderTests : TestBase
{
    private readonly IFeatureDefinitionManager _definitionManager = Substitute.For<IFeatureDefinitionManager>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ThreadCurrentPrincipalAccessor _principalAccessor = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly ClientFeaturesConfigBuilder _sut;

    public ClientFeaturesConfigBuilderTests()
    {
        _sut = new ClientFeaturesConfigBuilder(_definitionManager, _featureManager, _principalAccessor, _currentTenant);
    }

    [Fact]
    public async Task should_return_only_client_visible_feature_values()
    {
        // given
        var visible = new FeatureDefinition("Reports.Export");
        var hidden = new FeatureDefinition("Billing.Internal") { IsVisibleToClients = false };
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([visible, hidden]);
        _featureManager
            .GetAllAsync(Arg.Any<IReadOnlySet<string>>(), AbortToken)
            .Returns(call =>
                call.Arg<IReadOnlySet<string>>()
                    .ToDictionary(name => name, name => new FeatureValue(name, "true", null), StringComparer.Ordinal)
            );

        // when
        var config = await _sut.BuildAsync(_Context("tenant-1"), AbortToken);

        // then
        config
            .Values.Should()
            .BeEquivalentTo(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Reports.Export"] = "true" });
        await _featureManager
            .Received(1)
            .GetAllAsync(
                Arg.Is<IReadOnlySet<string>>(names => names.SetEquals(new[] { "Reports.Export" })),
                AbortToken
            );
    }

    [Fact]
    public async Task should_resolve_under_context_identity_and_restore_ambient_identity_afterwards()
    {
        // given
        var ambient = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "ambient")], "test"));
        var issued = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "issued")], "test"));
        using var ambientPrincipal = _principalAccessor.Change(ambient);
        using var ambientTenant = _currentTenant.Change("ambient-tenant");
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([new FeatureDefinition("Reports.Export")]);

        ClaimsPrincipal? principalDuringRead = null;
        string? tenantDuringRead = null;
        _featureManager
            .GetAllAsync(Arg.Any<IReadOnlySet<string>>(), AbortToken)
            .Returns(_ =>
            {
                principalDuringRead = _principalAccessor.Principal;
                tenantDuringRead = _currentTenant.Id;
                return new Dictionary<string, FeatureValue>(StringComparer.Ordinal);
            });

        // when
        await _sut.BuildAsync(new ClientConfigContext(issued, "issued-tenant"), AbortToken);

        // then
        principalDuringRead.Should().BeSameAs(issued);
        tenantDuringRead.Should().Be("issued-tenant");
        _principalAccessor.Principal.Should().BeSameAs(ambient);
        _currentTenant.Id.Should().Be("ambient-tenant");
    }

    [Fact]
    public async Task should_throw_when_context_is_null()
    {
        // when
        var act = () => _sut.BuildAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static ClientConfigContext _Context(string? tenantId)
    {
        return new ClientConfigContext(new ClaimsPrincipal(new ClaimsIdentity("test")), tenantId);
    }
}
