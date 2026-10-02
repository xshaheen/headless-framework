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
            catalog.AddBusConsumer<global::Billing.Shapes.InvoiceProjection, global::Billing.Shapes.InvoiceIssued>("billing.invoice-projection", everyInstance: false, dispatch: Dispatch_Billing_Shapes_InvoiceProjection);
            catalog.AddBusConsumer<global::Billing.Shapes.InvoiceProjection, global::Billing.Shapes.InvoicePaid>("billing.invoice-projection", everyInstance: false, dispatch: Dispatch_Billing_Shapes_InvoiceProjection);
            catalog.AddBusConsumer<global::Billing.Shapes.PriceCache, global::Billing.Shapes.PriceChanged>("billing.price-cache", everyInstance: true, dispatch: Dispatch_Billing_Shapes_PriceCache, onSubscriptionEstablished: OnSubscriptionEstablished_Billing_Shapes_PriceCache);
            catalog.AddQueueConsumer<global::Billing.Shapes.CloseDayConsumer, global::Billing.Shapes.CloseDay>("billing.close-day", dispatch: Dispatch_Billing_Shapes_CloseDayConsumer);
            catalog.AddQueueConsumer<global::Billing.Shapes.IssueInvoice, global::Billing.Shapes.IssueInvoiceCommand>("billing.issue-invoice", dispatch: Dispatch_Billing_Shapes_IssueInvoice);
            catalog.AddQueueConsumer<global::Billing.Shapes.RebuildConsumer, global::Billing.Shapes.Rebuild>("billing.rebuild", dispatch: Dispatch_Billing_Shapes_RebuildConsumer);
        }

        private static global::Billing.Shapes.InvoiceProjection Create_Billing_Shapes_InvoiceProjection(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Shapes.InvoiceProjection>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.InvoiceProjection>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_InvoiceProjection(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_InvoiceProjection(services, out _);
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

        private static global::Billing.Shapes.PriceCache Create_Billing_Shapes_PriceCache(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Shapes.PriceCache>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.PriceCache>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_PriceCache(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_PriceCache(services, out var created);
            try
            {
                switch (context)
                {
                    case global::Headless.Messaging.ConsumeContext<global::Billing.Shapes.PriceChanged> typed:
                        await ((global::Headless.Messaging.IConsume<global::Billing.Shapes.PriceChanged>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        throw new global::System.InvalidOperationException("Consumer Billing.Shapes.PriceCache does not consume " + context.MessageType.FullName + ".");
                }
            }
            finally
            {
                if (created)
                {
                    ((global::System.IDisposable)consumer).Dispose();
                }
            }
        }

        private static async global::System.Threading.Tasks.ValueTask OnSubscriptionEstablished_Billing_Shapes_PriceCache(global::System.IServiceProvider services, global::Headless.Messaging.SubscriptionEstablishedContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_PriceCache(services, out var created);
            try
            {
                await ((global::Headless.Messaging.IOnSubscriptionEstablished)consumer).OnSubscriptionEstablishedAsync(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (created)
                {
                    ((global::System.IDisposable)consumer).Dispose();
                }
            }
        }

        private static global::Billing.Shapes.CloseDayConsumer Create_Billing_Shapes_CloseDayConsumer(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Shapes.CloseDayConsumer>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.CloseDayConsumer>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_CloseDayConsumer(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_CloseDayConsumer(services, out _);
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

        private static global::Billing.Shapes.IssueInvoice Create_Billing_Shapes_IssueInvoice(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Shapes.IssueInvoice>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.IssueInvoice>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_IssueInvoice(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_IssueInvoice(services, out var created);
            try
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
            finally
            {
                if (created)
                {
                    await ((global::System.IAsyncDisposable)consumer).DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static global::Billing.Shapes.RebuildConsumer Create_Billing_Shapes_RebuildConsumer(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Shapes.RebuildConsumer>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Shapes.RebuildConsumer>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Shapes_RebuildConsumer(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Shapes_RebuildConsumer(services, out var created);
            try
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
            finally
            {
                if (created)
                {
                    await ((global::System.IAsyncDisposable)consumer).DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
