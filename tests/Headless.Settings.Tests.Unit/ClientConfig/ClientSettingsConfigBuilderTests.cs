// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Settings.ClientConfig;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.ClientConfig;

public sealed class ClientSettingsConfigBuilderTests : TestBase
{
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly ISettingManager _settingManager = Substitute.For<ISettingManager>();
    private readonly ThreadCurrentPrincipalAccessor _principalAccessor = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly ClientSettingsConfigBuilder _sut;

    public ClientSettingsConfigBuilderTests()
    {
        _sut = new ClientSettingsConfigBuilder(_definitionManager, _settingManager, _principalAccessor, _currentTenant);
    }

    [Fact]
    public async Task should_return_only_client_visible_setting_values()
    {
        // given
        var visible = new SettingDefinition("Localization.DefaultLanguage", isVisibleToClients: true);
        var hidden = new SettingDefinition("Smtp.Password");
        _definitionManager.GetAllAsync(AbortToken).Returns([visible, hidden]);
        _settingManager
            .GetAllAsync(Arg.Any<HashSet<string>>(), AbortToken)
            .Returns(call =>
                call.Arg<HashSet<string>>()
                    .ToDictionary(name => name, name => new SettingValue(name, "ar"), StringComparer.Ordinal)
            );

        // when
        var config = await _sut.BuildAsync(_Context("tenant-1"), AbortToken);

        // then
        config
            .Values.Should()
            .BeEquivalentTo(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Localization.DefaultLanguage"] = "ar" }
            );
        await _settingManager
            .Received(1)
            .GetAllAsync(
                Arg.Is<HashSet<string>>(names => names.SetEquals(new[] { "Localization.DefaultLanguage" })),
                AbortToken
            );
    }

    [Fact]
    public async Task should_return_empty_without_reading_values_when_no_setting_is_client_visible()
    {
        // given
        _definitionManager.GetAllAsync(AbortToken).Returns([new SettingDefinition("Smtp.Password")]);

        // when
        var config = await _sut.BuildAsync(_Context(tenantId: null), AbortToken);

        // then
        config.Values.Should().BeEmpty();
        await _settingManager.DidNotReceive().GetAllAsync(Arg.Any<HashSet<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_resolve_under_context_identity_and_restore_ambient_identity_afterwards()
    {
        // given
        var ambient = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "ambient")], "test"));
        var issued = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "issued")], "test"));
        using var ambientPrincipal = _principalAccessor.Change(ambient);
        using var ambientTenant = _currentTenant.Change("ambient-tenant");
        _definitionManager
            .GetAllAsync(AbortToken)
            .Returns([new SettingDefinition("Timing.TimeZone", isVisibleToClients: true)]);

        ClaimsPrincipal? principalDuringRead = null;
        string? tenantDuringRead = null;
        _settingManager
            .GetAllAsync(Arg.Any<HashSet<string>>(), AbortToken)
            .Returns(_ =>
            {
                principalDuringRead = _principalAccessor.Principal;
                tenantDuringRead = _currentTenant.Id;
                return new Dictionary<string, SettingValue>(StringComparer.Ordinal);
            });

        // when
        await _sut.BuildAsync(new ClientConfigContext(issued, "issued-tenant"), AbortToken);

        // then
        principalDuringRead.Should().BeSameAs(issued);
        tenantDuringRead.Should().Be("issued-tenant");
        _principalAccessor.Principal.Should().BeSameAs(ambient);
        _currentTenant.Id.Should().Be("ambient-tenant");
    }

    private static ClientConfigContext _Context(string? tenantId)
    {
        return new ClientConfigContext(new ClaimsPrincipal(new ClaimsIdentity("test")), tenantId);
    }
}
