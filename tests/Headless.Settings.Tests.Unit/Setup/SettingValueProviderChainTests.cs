// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.MultiTenancy;
using Headless.Settings;
using Headless.Settings.Resources;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

/// <summary>
/// A custom value provider lands where its registration asks, whether the app registers it before or after
/// <c>AddHeadlessSettings</c>.
/// </summary>
public sealed class SettingValueProviderChainTests : TestBase
{
    private const string _Setting = "Theme";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_place_provider_between_tenant_and_user_when_added_below_user(bool beforeAddHeadlessSettings)
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        if (beforeAddHeadlessSettings)
        {
            builder.Services.AddSettingValueProviderBelow<AccountSettingValueProvider, UserSettingValueProvider>();
            builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());
        }
        else
        {
            builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());
            builder.Services.AddSettingValueProviderBelow<AccountSettingValueProvider, UserSettingValueProvider>();
        }

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then - the list runs lowest priority first
        _ValueProviders(serviceProvider)
            .Should()
            .Equal(
                typeof(DefaultValueSettingValueProvider),
                typeof(ConfigurationSettingValueProvider),
                typeof(GlobalSettingValueProvider),
                typeof(TenantSettingValueProvider),
                typeof(AccountSettingValueProvider),
                typeof(UserSettingValueProvider)
            );
    }

    [Fact]
    public void should_put_provider_on_top_when_added_before_add_headless_settings()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.Services.AddSettingValueProvider<AccountSettingValueProvider>();
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        _ValueProviders(serviceProvider).Should().HaveCount(6).And.EndWith(typeof(AccountSettingValueProvider));
    }

    [Fact]
    public void should_move_provider_already_on_top_when_added_below_another()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());
        builder.Services.AddSettingValueProvider<AccountSettingValueProvider>();

        // when
        builder.Services.AddSettingValueProviderBelow<AccountSettingValueProvider, TenantSettingValueProvider>();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        _ValueProviders(serviceProvider)
            .Should()
            .Equal(
                typeof(DefaultValueSettingValueProvider),
                typeof(ConfigurationSettingValueProvider),
                typeof(GlobalSettingValueProvider),
                typeof(AccountSettingValueProvider),
                typeof(TenantSettingValueProvider),
                typeof(UserSettingValueProvider)
            );
    }

    [Fact]
    public void should_throw_when_the_higher_provider_is_not_in_the_chain()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());
        builder.Services.AddSettingValueProviderBelow<AccountSettingValueProvider, UnregisteredSettingValueProvider>();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // when
        var act = () => _ValueProviders(serviceProvider);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(UnregisteredSettingValueProvider)}*");
    }

    [Fact]
    public void should_throw_when_a_provider_is_placed_below_itself()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddSettingValueProviderBelow<AccountSettingValueProvider, AccountSettingValueProvider>();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task should_fall_back_through_the_inserted_provider_to_lower_providers()
    {
        // given an Account provider below User, a stored global value, and an account value for one account only
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSettingValueProviderBelow<AccountSettingValueProvider, UserSettingValueProvider>();
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<ChainTestDbContext>());

        var store = Substitute.For<ISettingValueStore>();
        store
            .GetOrDefaultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        store
            .GetOrDefaultAsync(_Setting, SettingValueProviderNames.Global, null, Arg.Any<CancellationToken>())
            .Returns("dark");
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(Substitute.For<ICurrentUser>());
        builder.Services.AddSingleton<ICurrentTenant>(new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance));

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        serviceProvider.GetRequiredService<AccountSettingValueProvider>().SetValue("account-1", "light");
        var sut = _CreateManager(serviceProvider);

        // when
        var accountWithValue = await sut.GetAsync(
            _Setting,
            AccountSettingValueProvider.ProviderName,
            "account-1",
            cancellationToken: AbortToken
        );
        var accountWithoutValue = await sut.GetAsync(
            _Setting,
            AccountSettingValueProvider.ProviderName,
            "account-2",
            cancellationToken: AbortToken
        );

        // then the account's own value wins, and an account without one falls back past Tenant to Global
        accountWithValue.Value.Should().Be("light");
        accountWithoutValue.Value.Should().Be("dark");
    }

    private static List<Type> _ValueProviders(IServiceProvider serviceProvider)
    {
        return
        [
            .. serviceProvider.GetRequiredService<IOptions<SettingManagementProvidersOptions>>().Value.ValueProviders,
        ];
    }

    private static SettingManager _CreateManager(IServiceProvider serviceProvider)
    {
        var definitions = Substitute.For<ISettingDefinitionManager>();
        definitions.FindAsync(_Setting, Arg.Any<CancellationToken>()).Returns(new SettingDefinition(_Setting));

        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("host");

        return new SettingManager(
            definitions,
            serviceProvider.GetRequiredService<ISettingValueStore>(),
            serviceProvider.GetRequiredService<ISettingValueProviderManager>(),
            Substitute.For<ISettingEncryptionService>(),
            new DefaultSettingErrorsDescriptor(),
            hostIdentity,
            serviceProvider.GetRequiredService<ICurrentTenant>()
        );
    }

    private sealed class AccountSettingValueProvider : ISettingValueReadProvider
    {
        public const string ProviderName = "Account";

        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string Name => ProviderName;

        public void SetValue(string accountId, string value)
        {
            _values[accountId] = value;
        }

        public Task<string?> GetOrDefaultAsync(
            SettingDefinition setting,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(providerKey is null ? null : _values.GetValueOrDefault(providerKey));
        }

        public Task<List<SettingValue>> GetAllAsync(
            SettingDefinition[] settings,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                settings
                    .Select(setting => new SettingValue(
                        setting.Name,
                        providerKey is null ? null : _values.GetValueOrDefault(providerKey)
                    ))
                    .ToList()
            );
        }
    }

    private sealed class UnregisteredSettingValueProvider : ISettingValueReadProvider
    {
        public string Name => "Unregistered";

        public Task<string?> GetOrDefaultAsync(
            SettingDefinition setting,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<string?>(null);
        }

        public Task<List<SettingValue>> GetAllAsync(
            SettingDefinition[] settings,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(settings.Select(setting => new SettingValue(setting.Name, null)).ToList());
        }
    }

    private sealed class ChainTestDbContext(DbContextOptions<ChainTestDbContext> options) : DbContext(options);
}
