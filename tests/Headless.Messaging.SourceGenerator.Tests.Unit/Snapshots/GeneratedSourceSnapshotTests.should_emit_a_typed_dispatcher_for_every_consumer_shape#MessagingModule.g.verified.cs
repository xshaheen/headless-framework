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
            catalog.AddBusConsumer<global::Billing.Shapes.InvoiceProjection, global::Billing.Shapes.InvoiceIssued>("billing.invoice-projection", everyInstance: false, policy: null, dispatch: Dispatch_Billing_Shapes_InvoiceProjection);
            catalog.AddBusConsumer<global::Billing.Shapes.InvoiceProjection, global::Billing.Shapes.InvoicePaid>("billing.invoice-projection", everyInstance: false, policy: null, dispatch: Dispatch_Billing_Shapes_InvoiceProjection);
            catalog.AddBusConsumer<global::Billing.Shapes.PriceCache, global::Billing.Shapes.PriceChanged>("billing.price-cache", everyInstance: true, policy: typeof(global::Billing.Shapes.CachePolicy), dispatch: Dispatch_Billing_Shapes_PriceCache);
            catalog.AddQueueConsumer<global::Billing.Shapes.CloseDayConsumer, global::Billing.Shapes.CloseDay>("billing.close-day", policy: null, dispatch: Dispatch_Billing_Shapes_CloseDayConsumer);
            catalog.AddQueueConsumer<global::Billing.Shapes.IssueInvoice, global::Billing.Shapes.IssueInvoiceCommand>("billing.issue-invoice", policy: null, dispatch: Dispatch_Billing_Shapes_IssueInvoice);
            catalog.AddQueueConsumer<global::Billing.Shapes.RebuildConsumer, global::Billing.Shapes.Rebuild>("billing.rebuild", policy: null, dispatch: Dispatch_Billing_Shapes_RebuildConsumer);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_InvoiceProjection(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.InvoiceProjection>(services);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.InvoiceIssued> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.InvoiceIssued>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.InvoicePaid> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.InvoicePaid>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Shapes.InvoiceProjection does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_PriceCache(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            using var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.PriceCache>(services);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.PriceChanged> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.PriceChanged>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Shapes.PriceCache does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_CloseDayConsumer(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.CloseDayConsumer>(services);
            await ((global::Headless.Messaging.IConsumerLifecycle)consumer).OnStartingAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                switch (context)
                {
                    case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.CloseDay> typed:
                        await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.CloseDay>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        throw new global::System.InvalidOperationException("Consumer Billing.Shapes.CloseDayConsumer does not consume " + context.MessageType.FullName + ".");
                }
            }
            finally
            {
                try
                {
                    await ((global::Headless.Messaging.IConsumerLifecycle)consumer).OnStoppingAsync(cancellationToken).ConfigureAwait(false);
                }
                #pragma warning disable ERP022 // A failing stop hook must not mask the delivery's outcome; the hook logs its own failures.
                catch
                {
                    // The delivery's own outcome wins.
                }
                #pragma warning restore ERP022
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_IssueInvoice(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.IssueInvoice>(services);
            await using (global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(consumer, false))
            {
                switch (context)
                {
                    case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.IssueInvoiceCommand> typed:
                        await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.IssueInvoiceCommand>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        throw new global::System.InvalidOperationException("Consumer Billing.Shapes.IssueInvoice does not consume " + context.MessageType.FullName + ".");
                }
            }
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_RebuildConsumer(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.RebuildConsumer>(services);
            await using (global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(consumer, false))
            {
                await ((global::Headless.Messaging.IConsumerLifecycle)consumer).OnStartingAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    switch (context)
                    {
                        case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.Rebuild> typed:
                            await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.Rebuild>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                            return;
                        default:
                            throw new global::System.InvalidOperationException("Consumer Billing.Shapes.RebuildConsumer does not consume " + context.MessageType.FullName + ".");
                    }
                }
                finally
                {
                    try
                    {
                        await ((global::Headless.Messaging.IConsumerLifecycle)consumer).OnStoppingAsync(cancellationToken).ConfigureAwait(false);
                    }
                    #pragma warning disable ERP022 // A failing stop hook must not mask the delivery's outcome; the hook logs its own failures.
                    catch
                    {
                        // The delivery's own outcome wins.
                    }
                    #pragma warning restore ERP022
                }
            }
        }
    }
}
