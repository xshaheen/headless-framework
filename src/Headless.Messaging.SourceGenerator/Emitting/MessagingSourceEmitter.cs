// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator.Models;
using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Emitting;

#pragma warning disable MA0076 // Generated source lines are clearer as interpolated templates.

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
    private const string _Lifecycle = "global::Headless.Messaging.IConsumerLifecycle";

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
            _WriteDispatcher(writer, registration);
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
            var policy = consumer.PolicyTypeName is null ? "null" : $"typeof({consumer.PolicyTypeName})";
            foreach (var message in consumer.MessageTypeNames)
            {
                writer.AppendLine(
                    consumer.Lane == ConsumerLane.Bus
                        ? $"catalog.AddBusConsumer<{consumer.TypeName}, {message}>({HandlerSource.Literal(consumer.Identity)}, everyInstance: {(consumer.EveryInstance ? "true" : "false")}, policy: {policy}, dispatch: {registration.DispatcherName});"
                        : $"catalog.AddQueueConsumer<{consumer.TypeName}, {message}>({HandlerSource.Literal(consumer.Identity)}, policy: {policy}, dispatch: {registration.DispatcherName});"
                );
            }
        }

        writer.CloseBracket();
    }

    /// <summary>
    /// Emits the typed dispatcher of one consumer class: it builds the class from the delivery's scope, so constructor
    /// dependencies resolve like any scoped service, runs the lifecycle hooks around the delivery the same way the
    /// runtime does for any consumer, calls <c>ConsumeAsync</c> through the interface that matches the context's message
    /// type so explicit implementations work too, and releases the instance it created.
    /// </summary>
    private static void _WriteDispatcher(SourceCodeBuilder writer, ConsumerRegistrationModel registration)
    {
        var consumer = registration.Consumer;
        writer.AppendLine(
            $"private static async global::System.Threading.Tasks.ValueTask {registration.DispatcherName}(global::System.IServiceProvider services, {_ConsumeContext} context, global::System.Threading.CancellationToken cancellationToken)"
        );
        writer.OpenBracket();

        writer.AppendHandlerInstance(
            consumer.Disposal,
            "consumer",
            $"global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{consumer.TypeName}>(services)",
            // The ConfigureAwait extension is called statically, because the generated file has no using directives.
            "global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(consumer, false)",
            w => _WriteDelivery(w, consumer)
        );

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
        // Consumer projects often run ErrorProne.NET, which analyzes generated code too; the swallow is deliberate, as in
        // the runtime's own dispatcher, so the pragma keeps it from failing their build.
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

        writer.AppendLine("default:");
        writer.IncreaseIndentation();
        writer.AppendLine(
            $"throw new global::System.InvalidOperationException({HandlerSource.Literal($"Consumer {consumer.DisplayName} does not consume ")} + context.MessageType.FullName + \".\");"
        );
        writer.RemoveIndentations();
        writer.CloseBracket();
    }
}

#pragma warning restore MA0076
