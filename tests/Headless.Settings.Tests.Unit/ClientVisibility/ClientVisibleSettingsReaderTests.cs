// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Settings.ClientVisibility;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.ClientVisibility;

public sealed class ClientVisibleSettingsReaderTests : TestBase
{
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly ISettingManager _settingManager = Substitute.For<ISettingManager>();
    private readonly ThreadCurrentPrincipalAccessor _principalAccessor = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly ClientVisibleSettingsReader _sut;

    public ClientVisibleSettingsReaderTests()
    {
        _sut = new ClientVisibleSettingsReader(_definitionManager, _settingManager, _currentTenant, _principalAccessor);
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
        var values = await _sut.GetAsync(_Context("tenant-1"), AbortToken);

        // then
        values
            .Should()
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
        var values = await _sut.GetAsync(_Context(tenantId: null), AbortToken);

        // then
        values.Should().BeEmpty();
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
        await _sut.GetAsync(new PrincipalContext(issued, "issued-tenant"), AbortToken);

        // then
        principalDuringRead.Should().BeSameAs(issued);
        tenantDuringRead.Should().Be("issued-tenant");
        _principalAccessor.Principal.Should().BeSameAs(ambient);
        _currentTenant.Id.Should().Be("ambient-tenant");
    }

    [Fact]
    public async Task should_read_when_no_principal_accessor_is_registered()
    {
        // given — only Headless.Api.ServiceDefaults registers an accessor; a worker host has none.
        var sut = new ClientVisibleSettingsReader(_definitionManager, _settingManager, _currentTenant);
        _definitionManager.GetAllAsync(AbortToken).Returns([new SettingDefinition("Smtp.Password")]);

        // when
        var values = await sut.GetAsync(_Context("tenant-1"), AbortToken);

        // then
        values.Should().BeEmpty();
    }

    private static PrincipalContext _Context(string? tenantId)
    {
        return new PrincipalContext(new ClaimsPrincipal(new ClaimsIdentity("test")), tenantId);
    }
}
