// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator.Models;
using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Emitting;

/// <summary>
/// Writes the per-assembly registration source from a <see cref="MessagingRegistrationModel"/>. This is the generator's
/// only emit path.
/// </summary>
/// <remarks>
/// Every type is written fully qualified, so the generated file never depends on a <see langword="using"/> and cannot be captured
/// by a namespace that shares a segment with the assembly name.
/// </remarks>
internal static class MessagingSourceEmitter
{
    /// <summary>The generated module's type name, in the namespace named after the assembly.</summary>
    public const string ModuleClassName = "MessagingModule";

    private const string _ConsumeContext = "global::Headless.Messaging.ConsumeContext";
    private const string _Consume = "global::Headless.Messaging.IConsume";
    private const string _Respond = "global::Headless.Messaging.IRespond";
    private const string _Lifecycle = "global::Headless.Messaging.IConsumerLifecycle";
    private const string _SubscriptionHook = "global::Headless.Messaging.IOnSubscriptionEstablished";
    private const string _ServiceProvider = "global::System.IServiceProvider";
    private const string _CancellationToken = "global::System.Threading.CancellationToken";

    public static string Emit(MessagingRegistrationModel model)
    {
        var writer = new SourceCodeBuilder();

        writer.AppendLine("//Messaging readonly auto-generated file.");
        writer.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        writer.NewLine();
        writer.AppendLine($"namespace {model.AssemblyName}");
        writer.OpenBracket();
        // Public so a module's entry point can name it in AddModule<T>(); the registration itself is an explicit
        // interface implementation, reachable only through that call, which runs it once per host.
        writer.AppendLine(
            "/// <summary>Generated Messaging registration for this assembly. Add it with <c>AddModule&lt;MessagingModule&gt;()</c>.</summary>"
        );
        writer.AppendLine($"public sealed class {ModuleClassName} : global::Headless.Messaging.IMessagingModule");
        writer.OpenBracket();
        writer.AppendLine($"private {ModuleClassName}() {{ }}");
        writer.NewLine();
        _WriteRegister(writer, model);

        foreach (var registration in model.Consumers)
        {
            writer.NewLine();
            _WriteFactory(writer, registration);
            writer.NewLine();
            _WriteDispatcher(writer, registration);

            if (registration.Consumer.HasSubscriptionHook)
            {
                writer.NewLine();
                _WriteSubscriptionHook(writer, registration);
            }
        }

        writer.CloseBracket();
        writer.CloseBracket();

        return writer.ToString();
    }

    private static void _WriteRegister(SourceCodeBuilder writer, MessagingRegistrationModel model)
    {
        writer.AppendLine(
            "static void global::Headless.Messaging.IMessagingModule.Register(global::Headless.Messaging.MessagingCatalogBuilder catalog)"
        );
        writer.OpenBracket();

        foreach (var registration in model.Consumers)
        {
            var consumer = registration.Consumer;
            foreach (var message in consumer.MessageTypeNames)
            {
                var hook = consumer.HasSubscriptionHook
                    ? $", onSubscriptionEstablished: {registration.SubscriptionHookName}"
                    : "";
                writer.AppendLine(
                    consumer.Lane == ConsumerLane.Bus
                        ? $"catalog.AddBusConsumer<{consumer.TypeName}, {message}>({HandlerSource.Literal(consumer.Identity)}, everyInstance: {(consumer.EveryInstance ? "true" : "false")}, dispatch: {registration.DispatcherName}{hook});"
                        : $"catalog.AddQueueConsumer<{consumer.TypeName}, {message}>({HandlerSource.Literal(consumer.Identity)}, dispatch: {registration.DispatcherName});"
                );
            }

            // Only a Queue class reaches emission with responders: a Bus responder fails the build.
            foreach (var responder in consumer.Responders)
            {
                writer.AppendLine(
                    $"catalog.AddQueueResponder<{consumer.TypeName}, {responder.RequestTypeName}, {responder.ResponseTypeName}>({HandlerSource.Literal(consumer.Identity)}, dispatch: {registration.DispatcherName});"
                );
            }
        }

        writer.CloseBracket();
    }

    /// <summary>
    /// Emits the factory every generated call of one consumer class builds it through. A class the application registered,
    /// or decorated, in the container resolves from the call's scope, which owns it; otherwise the factory constructs it
    /// with its dependencies from that scope and reports that the caller owns the instance.
    /// </summary>
    private static void _WriteFactory(SourceCodeBuilder writer, ConsumerRegistrationModel registration)
    {
        var consumer = registration.Consumer;
        writer.AppendLine(
            $"private static {consumer.TypeName} {registration.FactoryName}({_ServiceProvider} services, out bool created)"
        );
        writer.OpenBracket();
        writer.AppendLine(
            $"var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{consumer.TypeName}>(services);"
        );
        writer.AppendLine("created = registered is null;");
        writer.AppendLine(
            $"return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{consumer.TypeName}>(services);"
        );
        writer.CloseBracket();
    }

    /// <summary>
    /// Emits the typed dispatcher of one consumer class: it builds the class through its factory from the delivery's
    /// scope, runs the <c>IConsumerLifecycle</c> hooks around the delivery, calls <c>ConsumeAsync</c> or
    /// <c>RespondAsync</c> through the interface that matches the context's message type so explicit implementations work
    /// too, and releases an instance it created.
    /// </summary>
    private static void _WriteDispatcher(SourceCodeBuilder writer, ConsumerRegistrationModel registration)
    {
        var consumer = registration.Consumer;
        writer.AppendLine(
            $"private static async global::System.Threading.Tasks.ValueTask {registration.DispatcherName}({_ServiceProvider} services, {_ConsumeContext} context, {_CancellationToken} cancellationToken)"
        );
        writer.OpenBracket();
        _WriteOwnedInstance(writer, registration, w => _WriteDelivery(w, consumer));
        writer.CloseBracket();
    }

    /// <summary>
    /// Emits the <c>IOnSubscriptionEstablished</c> call of one every-instance consumer class, built through the same
    /// factory as its deliveries.
    /// </summary>
    private static void _WriteSubscriptionHook(SourceCodeBuilder writer, ConsumerRegistrationModel registration)
    {
        writer.AppendLine(
            $"private static async global::System.Threading.Tasks.ValueTask {registration.SubscriptionHookName}({_ServiceProvider} services, global::Headless.Messaging.SubscriptionEstablishedContext context, {_CancellationToken} cancellationToken)"
        );
        writer.OpenBracket();
        _WriteOwnedInstance(
            writer,
            registration,
            static w =>
                w.AppendLine(
                    $"await (({_SubscriptionHook})consumer).OnSubscriptionEstablishedAsync(context, cancellationToken).ConfigureAwait(false);"
                )
        );
        writer.CloseBracket();
    }

    /// <summary>
    /// Builds the class through its factory, runs <paramref name="writeBody"/>, and disposes the instance only when the
    /// factory created it: a container-resolved instance belongs to the scope.
    /// </summary>
    private static void _WriteOwnedInstance(
        SourceCodeBuilder writer,
        ConsumerRegistrationModel registration,
        Action<SourceCodeBuilder> writeBody
    )
    {
        var disposal = registration.Consumer.Disposal;
        if (disposal is HandlerDisposal.None)
        {
            writer.AppendLine($"var consumer = {registration.FactoryName}(services, out _);");
            writeBody(writer);
            return;
        }

        writer.AppendLine($"var consumer = {registration.FactoryName}(services, out var created);");
        writer.AppendLine("try");
        writer.OpenBracket();
        writeBody(writer);
        writer.CloseBracket();
        writer.AppendLine("finally");
        writer.OpenBracket();
        writer.AppendLine("if (created)");
        writer.OpenBracket();
        writer.AppendLine(
            disposal is HandlerDisposal.Async
                ? "await ((global::System.IAsyncDisposable)consumer).DisposeAsync().ConfigureAwait(false);"
                : "((global::System.IDisposable)consumer).Dispose();"
        );
        writer.CloseBracket();
        writer.CloseBracket();
    }

    private static void _WriteDelivery(SourceCodeBuilder writer, ConsumerModel consumer)
    {
        if (!consumer.HasLifecycle)
        {
            _WriteSwitch(writer, consumer);
            return;
        }

        // A failing start propagates and skips the stop hook; a failing stop never masks the delivery's own outcome.
        writer.AppendLine($"await (({_Lifecycle})consumer).OnStartingAsync(cancellationToken).ConfigureAwait(false);");
        writer.AppendLine("try");
        writer.OpenBracket();
        _WriteSwitch(writer, consumer);
        writer.CloseBracket();
        writer.AppendLine("finally");
        writer.OpenBracket();
        writer.AppendLine("try");
        writer.OpenBracket();
        writer.AppendLine($"await (({_Lifecycle})consumer).OnStoppingAsync(cancellationToken).ConfigureAwait(false);");
        writer.CloseBracket();
        // Consumer projects often run ErrorProne.NET, which analyzes generated code too; the swallow is deliberate, so the
        // pragma keeps it from failing their build.
        writer.AppendLine(
            "#pragma warning disable ERP022 // A failing stop hook must not mask the delivery's outcome; the hook logs its own failures."
        );
        writer.AppendLine("catch");
        writer.OpenBracket();
        writer.AppendLine("// The delivery's own outcome wins.");
        writer.CloseBracket();
        writer.AppendLine("#pragma warning restore ERP022");
        writer.CloseBracket();
    }

    private static void _WriteSwitch(SourceCodeBuilder writer, ConsumerModel consumer)
    {
        writer.AppendLine("switch (context)");
        writer.OpenBracket();
        foreach (var message in consumer.MessageTypeNames)
        {
            writer.AppendLine($"case {_ConsumeContext}<{message}> typed:");
            writer.IncreaseIndentation();
            writer.AppendLine(
                $"await (({_Consume}<{message}>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);"
            );
            writer.AppendLine("return;");
            writer.RemoveIndentations();
        }

        // The reply is recorded under the declared response type, which names the reply's contract, rather than the
        // runtime type of the returned value. A null result is recorded too, so messaging can tell it from no reply.
        foreach (var responder in consumer.Responders)
        {
            writer.AppendLine($"case {_ConsumeContext}<{responder.RequestTypeName}> typed:");
            writer.IncreaseIndentation();
            writer.AppendLine(
                $"typed.RecordReply<{responder.ResponseTypeName}>(await (({_Respond}<{responder.RequestTypeName}, {responder.ResponseTypeName}>)consumer).RespondAsync(typed, cancellationToken).ConfigureAwait(false));"
            );
            writer.AppendLine("return;");
            writer.RemoveIndentations();
        }

        writer.AppendLine("default:");
        writer.IncreaseIndentation();
        writer.AppendLine(
            $"throw new global::System.InvalidOperationException({HandlerSource.Literal($"Consumer {consumer.DisplayName} does not consume ")} + context.MessageType.FullName + \".\");"
        );
        writer.RemoveIndentations();
        writer.CloseBracket();
    }
}
