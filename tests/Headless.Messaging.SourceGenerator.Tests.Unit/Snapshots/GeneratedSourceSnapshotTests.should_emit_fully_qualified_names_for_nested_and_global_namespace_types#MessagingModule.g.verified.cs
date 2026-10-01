//HintName: MessagingModule.g.cs
//Messaging readonly auto-generated file.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Messaging.SourceGenerator.Tests
{
    /// <summary>Generated Messaging registration for this assembly. Add it with <c>AddModule&lt;MessagingModule&gt;()</c>.</summary>
    public sealed class MessagingModule : global::Headless.Messaging.IMessagingModule
    {
        private MessagingModule() { }

        static void global::Headless.Messaging.IMessagingModule.Register(global::Headless.Messaging.MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<global::Billing_Handlers.Nested, global::Billing.Payload>("billing.flattened", everyInstance: false, dispatch: Dispatch_Billing_Handlers_Nested);
            catalog.AddBusConsumer<global::Billing.Handlers.Nested, global::Billing.Payload>("billing.nested", everyInstance: false, dispatch: Dispatch_Billing_Handlers_Nested_2);
            catalog.AddBusConsumer<global::Billing.Handlers.Nested, global::GlobalMessage>("billing.nested", everyInstance: false, dispatch: Dispatch_Billing_Handlers_Nested_2);
            catalog.AddBusConsumer<global::GlobalConsumer, global::GlobalMessage>("root.global", everyInstance: false, dispatch: Dispatch_GlobalConsumer);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Handlers_Nested(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing_Handlers.Nested>(services);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Payload> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Payload>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing_Handlers.Nested does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Handlers_Nested_2(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Handlers.Nested>(services);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Payload> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Payload>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                case global::Headless.Messaging.ConsumeContext<global::GlobalMessage> typed:
                    await ((global::Headless.Messaging.IConsume<global::GlobalMessage>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Handlers.Nested does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_GlobalConsumer(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::GlobalConsumer>(services);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::GlobalMessage> typed:
                    await ((global::Headless.Messaging.IConsume<global::GlobalMessage>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer GlobalConsumer does not consume " + context.MessageType.FullName + ".");
            }
        }
    }
}
