// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.Loader;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>What the consumers compiled by <see cref="GeneratedDispatchTests"/> did, read back by the test.</summary>
public sealed class DispatchProbe
{
    public List<string> Calls { get; } = [];
}

/// <summary>
/// Runs generated dispatchers: compiles a consumer assembly through the generator, loads it, registers its module into a
/// catalog, and invokes the dispatch the catalog received, so the test sees what the emitted code does at run time.
/// </summary>
public sealed class GeneratedDispatchTests : TestBase
{
    private const string _Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;

        namespace Billing;

        public sealed record InvoiceIssued(string Number);
        public sealed record InvoicePaid(string Number);

        [BusConsumer("billing.invoice-projection")]
        public sealed class InvoiceProjection(Tests.DispatchProbe probe)
            : IConsume<InvoiceIssued>, IConsume<InvoicePaid>, IConsumerLifecycle, IDisposable
        {
            ValueTask IConsume<InvoiceIssued>.ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("issued:" + context.Message.Number);
                return default;
            }

            public ValueTask ConsumeAsync(ConsumeContext<InvoicePaid> context, CancellationToken cancellationToken)
            {
                probe.Calls.Add("paid:" + context.Message.Number);
                return default;
            }

            public ValueTask OnStartingAsync(CancellationToken cancellationToken)
            {
                probe.Calls.Add("starting");
                return default;
            }

            public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
            {
                probe.Calls.Add("stopping");
                throw new InvalidOperationException("A failing stop hook must not mask the delivery.");
            }

            public void Dispose() => probe.Calls.Add("disposed");
        }
        """;

    [Fact]
    public async Task should_invoke_the_consume_overload_that_matches_the_typed_context()
    {
        // given
        var declarations = _RegisterGeneratedModule(out var messageTypes);
        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var probe = provider.GetRequiredService<DispatchProbe>();

        // when
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoicePaid"], "INV-2"), AbortToken);
        await declarations[0].Dispatch(provider, _Context(messageTypes["InvoiceIssued"], "INV-1"), AbortToken);

        // then
        probe
            .Calls.Should()
            .Equal(
                "starting",
                "paid:INV-2",
                "stopping",
                "disposed",
                "starting",
                "issued:INV-1",
                "stopping",
                "disposed"
            );
    }

    [Fact]
    public async Task should_hand_every_message_of_a_class_its_one_dispatcher_and_reject_other_messages()
    {
        // when
        var declarations = _RegisterGeneratedModule(out _);

        // then
        declarations.Select(x => x.MessageType.Name).Should().Equal("InvoiceIssued", "InvoicePaid");
        declarations.Should().OnlyContain(x => x.Identity == "billing.invoice-projection" && x.Lane == MessageLane.Bus);
        declarations[0].Dispatch.Should().Be(declarations[1].Dispatch);

        await using var provider = new ServiceCollection().AddSingleton<DispatchProbe>().BuildServiceProvider();
        var unrelated = _Context(typeof(UnrelatedMessage), "X-1");
        var dispatch = () => declarations[0].Dispatch(provider, unrelated, AbortToken).AsTask();
        await dispatch
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*InvoiceProjection*UnrelatedMessage*");
    }

    public sealed record UnrelatedMessage(string Number);

    private static IReadOnlyList<MessagingConsumerDeclaration> _RegisterGeneratedModule(
        out Dictionary<string, Type> messageTypes
    )
    {
        GeneratorTestHelper.Run(_Source, out var diagnostics, out var compilation);
        diagnostics.Should().NotContain(x => x.Severity == DiagnosticSeverity.Error);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;

        // A collectible context keeps each compiled copy of the source out of the test process's default context.
        var assembly = new AssemblyLoadContext(name: null, isCollectible: true).LoadFromStream(stream);
        messageTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["InvoiceIssued"] = assembly.GetType("Billing.InvoiceIssued", throwOnError: true)!,
            ["InvoicePaid"] = assembly.GetType("Billing.InvoicePaid", throwOnError: true)!,
        };

        var module = assembly.GetType($"{GeneratorTestHelper.AssemblyName}.MessagingModule", throwOnError: true)!;
        var register = module.GetInterfaceMap(typeof(IMessagingModule)).TargetMethods.Single();
        var catalog = new MessagingCatalogBuilder();
        catalog.AddModule(module, builder => register.Invoke(null, [builder]));

        return catalog.Consumers;
    }

    /// <summary>Builds the typed context the runtime would hand the dispatcher for one message.</summary>
    private static ConsumeContext _Context(Type messageType, string number)
    {
        var contextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var context = (ConsumeContext)Activator.CreateInstance(contextType)!;
        contextType
            .GetProperty(
                nameof(ConsumeContext<>.Message),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(context, Activator.CreateInstance(messageType, number));
        return context;
    }
}
