// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Auth;
using Headless.Payments.Paymob.CashIn.Models.Callback;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using Headless.Payments.Paymob.CashIn.Models.Merchant;
using Headless.Payments.Paymob.CashIn.Models.Orders;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.CashIn.Models.Refunds;
using Headless.Payments.Paymob.CashIn.Models.Transactions;

namespace Headless.Payments.Paymob.CashIn.Internals;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter), typeof(AddNullableAmountConverter)]
)]
// Auth
[JsonSerializable(typeof(CashInAuthenticationTokenRequest))]
[JsonSerializable(typeof(CashInAuthenticationTokenResponse))]
// Callback
[JsonSerializable(typeof(CashInCallback))]
[JsonSerializable(typeof(CashInCallbackQueryParameters))]
[JsonSerializable(typeof(CashInCallbackToken))]
[JsonSerializable(typeof(CashInCallbackTransaction))]
[JsonSerializable(typeof(CashInCallbackTransactionData))]
[JsonSerializable(typeof(CashInCallbackTransactionDataAcquirer))]
[JsonSerializable(typeof(CashInCallbackTransactionDataMigsOrder))]
[JsonSerializable(typeof(CashInCallbackTransactionDataMigsTransaction))]
[JsonSerializable(typeof(CashInCallbackTransactionOrder))]
[JsonSerializable(typeof(CashInCallbackTransactionOrderCollector))]
[JsonSerializable(typeof(CashInCallbackTransactionOrderShippingDetails))]
[JsonSerializable(typeof(CashInCallbackTransactionSourceData))]
[JsonSerializable(typeof(CashInCardInfo))]
[JsonSerializable(typeof(TransactionProcessedCallbackResponse))]
[JsonSerializable(typeof(TransactionProcessedCallbackResponseObj))]
// Intentions
[JsonSerializable(typeof(CashInCreateIntentionRequest))]
[JsonSerializable(typeof(CashInCreateIntentionRequestBillingData))]
[JsonSerializable(typeof(CashInCreateIntentionRequestItem))]
[JsonSerializable(typeof(CashInCreateIntentionResponse))]
[JsonSerializable(typeof(CashInCreateIntentionResponseBillingData))]
[JsonSerializable(typeof(CashInCreateIntentionResponseCreationExtras))]
[JsonSerializable(typeof(CashInCreateIntentionResponseExtras))]
[JsonSerializable(typeof(CashInCreateIntentionResponseIntentionDetail))]
[JsonSerializable(typeof(CashInCreateIntentionResponseItem))]
[JsonSerializable(typeof(CashInCreateIntentionResponsePaymentKey))]
[JsonSerializable(typeof(CashInCreateIntentionResponsePaymentMethod))]
// Merchant
[JsonSerializable(typeof(CashInProfile))]
[JsonSerializable(typeof(CashInProfileUser))]
// Orders
[JsonSerializable(typeof(CashInCreateOrderInternalRequest))]
[JsonSerializable(typeof(CashInCreateOrderRequest))]
[JsonSerializable(typeof(CashInCreateOrderRequestOrderItem))]
[JsonSerializable(typeof(CashInCreateOrderRequestShippingData))]
[JsonSerializable(typeof(CashInCreateOrderRequestShippingDetails))]
[JsonSerializable(typeof(CashInCreateOrderResponse))]
[JsonSerializable(typeof(CashInOrder))]
[JsonSerializable(typeof(CashInOrderItem))]
[JsonSerializable(typeof(CashInOrderMerchant))]
[JsonSerializable(typeof(CashInOrderShippingData))]
[JsonSerializable(typeof(CashInOrderShippingDetails))]
[JsonSerializable(typeof(CashInOrdersPage))]
[JsonSerializable(typeof(CashInOrdersPageRequest))]
// Payment
[JsonSerializable(typeof(CashInBillingData))]
[JsonSerializable(typeof(CashInCashCollectionPayResponse))]
[JsonSerializable(typeof(CashInKioskPayData))]
[JsonSerializable(typeof(CashInKioskPayResponse))]
[JsonSerializable(typeof(CashInKioskPaySourceData))]
[JsonSerializable(typeof(CashInPaymentKeyInternalRequest))]
[JsonSerializable(typeof(CashInPaymentKeyRequest))]
[JsonSerializable(typeof(CashInPaymentKeyResponse))]
[JsonSerializable(typeof(CashInPayPaymentKeyClaims))]
[JsonSerializable(typeof(CashInPayPaymentKeyClaimsBillingData))]
[JsonSerializable(typeof(CashInPayRequest))]
[JsonSerializable(typeof(CashInSavedTokenPayResponse))]
[JsonSerializable(typeof(CashInSource))]
[JsonSerializable(typeof(CashInWalletData))]
[JsonSerializable(typeof(CashInWalletPayResponse))]
[JsonSerializable(typeof(CashInWalletPaySourceData))]
// Refunds
[JsonSerializable(typeof(CashInRefundRequest))]
[JsonSerializable(typeof(CashInVoidRefundRequest))]
// Transactions
[JsonSerializable(typeof(CashInTransaction))]
[JsonSerializable(typeof(CashInTransactionBillingData))]
[JsonSerializable(typeof(CashInTransactionData))]
[JsonSerializable(typeof(CashInTransactionSourceData))]
[JsonSerializable(typeof(CashInTransactionsPage))]
[JsonSerializable(typeof(CashInTransactionsPageRequest))]
internal sealed partial class PaymobCashInJsonSerializerContext : JsonSerializerContext;
