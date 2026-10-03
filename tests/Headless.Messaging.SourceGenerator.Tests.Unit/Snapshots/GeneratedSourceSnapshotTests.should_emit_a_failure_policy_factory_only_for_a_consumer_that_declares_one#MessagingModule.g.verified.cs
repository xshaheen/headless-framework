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
            catalog.AddBusConsumer<global::Billing.Policies.Ledger, global::Billing.Policies.CardCharged>("billing.ledger", everyInstance: false, dispatch: Dispatch_Billing_Policies_Ledger, failurePolicy: static () => new global::Billing.Policies.PaymentsPolicy());
            catalog.AddBusConsumer<global::Billing.Policies.Receipts, global::Billing.Policies.ReceiptPrinted>("billing.receipts", everyInstance: false, dispatch: Dispatch_Billing_Policies_Receipts);
            catalog.AddQueueConsumer<global::Billing.Policies.Charge, global::Billing.Policies.ChargeCard>("billing.charge", dispatch: Dispatch_Billing_Policies_Charge, failurePolicy: static () => new global::Billing.Policies.PaymentsPolicy());
        }

        private static global::Billing.Policies.Ledger Create_Billing_Policies_Ledger(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Policies.Ledger>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Policies.Ledger>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Policies_Ledger(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Policies_Ledger(services, out _);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Policies.CardCharged> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Policies.CardCharged>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Policies.Ledger does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static global::Billing.Policies.Receipts Create_Billing_Policies_Receipts(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Policies.Receipts>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Policies.Receipts>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Policies_Receipts(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Policies_Receipts(services, out _);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Policies.ReceiptPrinted> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Policies.ReceiptPrinted>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Policies.Receipts does not consume " + context.MessageType.FullName + ".");
            }
        }

        private static global::Billing.Policies.Charge Create_Billing_Policies_Charge(global::System.IServiceProvider services, out bool created)
        {
            var registered = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Billing.Policies.Charge>(services);
            created = registered is null;
            return registered ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<global::Billing.Policies.Charge>(services);
        }

        private static async global::System.Threading.Tasks.ValueTask Dispatch_Billing_Policies_Charge(global::System.IServiceProvider services, global::Headless.Messaging.ConsumeContext context, global::System.Threading.CancellationToken cancellationToken)
        {
            var consumer = Create_Billing_Policies_Charge(services, out _);
            switch (context)
            {
                case global::Headless.Messaging.ConsumeContext<global::Billing.Policies.ChargeCard> typed:
                    await ((global::Headless.Messaging.IConsume<global::Billing.Policies.ChargeCard>)consumer).ConsumeAsync(typed, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new global::System.InvalidOperationException("Consumer Billing.Policies.Charge does not consume " + context.MessageType.FullName + ".");
            }
        }
    }
}
