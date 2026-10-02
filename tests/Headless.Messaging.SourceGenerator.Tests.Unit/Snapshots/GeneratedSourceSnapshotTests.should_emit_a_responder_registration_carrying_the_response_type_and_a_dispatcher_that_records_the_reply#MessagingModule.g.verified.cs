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
            catalog.AddQueueConsumer<global::Pricing.PricingDesk, global::Pricing.RefreshPrices>("pricing.desk", dispatch: Dispatch_Pricing_PricingDesk);
            catalog.AddQueueResponder<global::Pricing.PricingDesk, global::Pricing.GetPriceList, global::Pricing.PriceList>("pricing.desk", dispatch: Dispatch_Pricing_PricingDesk);
            catalog.AddQueueResponder<global::Pricing.GetQuoteResponder, global::Pricing.GetQuote, global::Pricing.Quote>("pricing.get-quote", dispatch: Dispatch_Pricing_GetQuoteResponder);
        }

        private static global::Pricing.PricingDesk Create_Pricing_PricingDesk(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Pricing.PricingDesk>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Pricing.PricingDesk>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Pricing_PricingDesk(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Pricing_PricingDesk(services, out var created);
            try
            {
                await ((global::Headless.Messaging.IConsumerLifecycle)consumer).OnStartingAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    switch (context)
                    {
                        case global::Headless.Messaging.ConsumeContext<global::Pricing.RefreshPrices> typed:
                            await ((global::Headless.Messaging.IConsume<global::Pricing.RefreshPrices>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                            return;
                        case global::Headless.Messaging.ConsumeContext<global::Pricing.GetPriceList> typed:
                            typed.RecordReply<global::Pricing.PriceList>(await ((global::Headless.Messaging.IRespond<global::Pricing.GetPriceList, global::Pricing.PriceList>)consumer).RespondAsync(typed, cancellationToken).ConfigureAwait(false));
                            return;
                        default:
                            throw new global::System.InvalidOperationException("Consumer Pricing.PricingDesk does not consume " + context.MessageType.FullName + ".");
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

        private static global::Pricing.GetQuoteResponder Create_Pricing_GetQuoteResponder(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Pricing.GetQuoteResponder>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Pricing.GetQuoteResponder>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Pricing_GetQuoteResponder(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Pricing_GetQuoteResponder(services, out _);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Pricing.GetQuote> typed:
                    typed.RecordReply<global::Pricing.Quote>(await ((global::Headless.Messaging.IRespond<global::Pricing.GetQuote, global::Pricing.Quote>)consumer).RespondAsync(typed, cancellationToken).ConfigureAwait(false));
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Pricing.GetQuoteResponder does not consume " + context.MessageType.FullName + ".");
            }
        }
    }
}
