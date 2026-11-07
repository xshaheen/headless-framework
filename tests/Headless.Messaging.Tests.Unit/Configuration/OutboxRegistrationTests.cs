// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Helpers;

namespace Tests.Configuration;

public sealed class OutboxRegistrationTests : TestBase
{
    [Fact]
    public void should_refuse_a_second_outbox_with_the_same_name_naming_the_duplicate()
    {
        // given
        var services = new ServiceCollection();

        // when — the names differ only in case, which still names one store
        var act = () =>
            services.AddHeadlessMessaging(setup =>
            {
                setup.AddOutbox("billing").UseStorage(_ => AdditionalOutboxDoubles.CreateOutbox("billing"));
                setup.AddOutbox("Billing");
            });

        // then
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("An outbox named 'Billing' is already registered on this host*")
            .Which.ParamName.Should()
            .Be("name");
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("PRIMARY")]
    public void should_refuse_the_primary_storage_name(string name)
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessMessaging(setup => setup.AddOutbox(name));

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage($"Outbox name '{name}' is reserved for the primary storage*");
    }

    [Theory]
    [InlineData("billing eu")]
    [InlineData("")]
    [InlineData("billing.eu")]
    [InlineData("billing/eu")]
    [InlineData("فواتير")]
    public void should_refuse_a_name_that_is_not_a_slug(string name)
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessMessaging(setup => setup.AddOutbox(name));

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage($"Outbox name '{name}' must contain only letters, digits, '-', and '_'.*");
    }

    [Fact]
    public void should_refuse_a_null_name()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessMessaging(setup => setup.AddOutbox(null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_accept_letters_digits_dashes_and_underscores()
    {
        var services = new ServiceCollection();
        OutboxStorageBuilder? outbox = null;

        services.AddHeadlessMessaging(setup =>
        {
            outbox = setup.AddOutbox("Billing_EU-2");
            outbox.UseStorage(_ => AdditionalOutboxDoubles.CreateOutbox("billing"));
        });

        outbox!.Name.Should().Be("Billing_EU-2");
    }

    [Fact]
    public async Task should_list_the_primary_and_each_outbox_by_name_in_the_host_manifest()
    {
        // given — registered against alphabetical order, so a listing by position or by sorted name both fail
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup
                .AddOutbox("shipping")
                .UseStorage(_ => AdditionalOutboxDoubles.CreateOutbox("shipping_db", name: "shipping"));
            setup
                .AddOutbox("billing")
                .UseStorage(_ => AdditionalOutboxDoubles.CreateOutbox("billing_db", name: "billing"));
        });

        // An additional outbox needs a relational primary storage, which the in-memory storage is not.
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        services.RemoveAll<IDataStorage>();
        services.RemoveAll<IDeliveryCoordinationResolver>();
        services.AddSingleton(primary);
        services.AddSingleton((IDeliveryCoordinationResolver)primary);

        await using var provider = services.BuildServiceProvider();

        // when
        var outboxes = _Describe(provider).GetProperty("outboxes");

        // then
        outboxes
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .Should()
            .Equal("primary", "shipping", "billing");
    }

    private static JsonElement _Describe(IServiceProvider provider)
    {
        var section = provider
            .GetServices<IHostManifestSection>()
            .Should()
            .ContainSingle(x => x.Name == "messaging")
            .Subject;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            section.Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }
}
