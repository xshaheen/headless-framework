---
domain: Payments
packages: Payments.Paymob.CashIn, Payments.Paymob.CashOut, Payments.Paymob
---

# Payments

> Paymob integration for Egypt and MENA: collecting payments (cash-in), paying out funds (cash-out), and a typed service layer over both.

## Orientation

The three packages do different jobs. They are not alternative providers, and there is no generic payment abstraction.

| Package | Job | Registration |
| --- | --- | --- |
| `Headless.Payments.Paymob.CashIn` | Collect payments: cards, wallets, kiosk, cash collection, saved tokens. | `services.AddPaymobCashIn(...)` |
| `Headless.Payments.Paymob.CashOut` | Pay out to mobile wallets, bank wallets, bank cards and accounts, and Aman kiosks. | `services.AddPaymobCashOut(...)` |
| `Headless.Payments.Paymob` | Typed per-channel services over both brokers, fee calculation, and error mapping. | `services.AddPaymobServices()`, after the two calls above |

Install only the packages your job needs. `Headless.Payments.Paymob` references both broker packages, and most application code can use its services instead of the brokers.

```csharp
builder.Services.AddPaymobCashIn(builder.Configuration.GetSection("Paymob:CashIn"));
builder.Services.AddPaymobCashOut(builder.Configuration.GetSection("Paymob:CashOut"));
builder.Services.AddPaymobServices();
```

**Verify:** start the host. A missing credential, a non-HTTPS URL, or an out-of-range value fails the start with `OptionsValidationException`. Under `RunHeadlessAsync`, `dotnet MyApp.dll check` prints `Host check failed: ...` and exits 1 for the same mistake. Validation does not prove the credentials work: the first call to Paymob with a bad credential returns a rejected result whose error carries status `401` or `403`.

## Agent Rules

- Use `IPaymobCashInService` for card, wallet, and kiosk cash-in flows. Use `IPaymobCashInBroker` only for raw Paymob calls the service does not cover, such as order management or the legacy order and payment-key flow.
- Use `ICashOutService` for payouts. It builds the right request per channel and maps Paymob's status and error codes into `CashOutResult<T>`.
- Configure cash-in and cash-out with separate credentials. `PaymobCashInOptions` needs `ApiKey`, `Hmac`, and `SecretKey`. `PaymobCashOutOptions` needs `ApiBaseUrl`, `UserName`, `Password`, `ClientId`, and `ClientSecret`.
- Bind the cash-out options from configuration. Its credential properties are `init`-only, so an `Action<PaymobCashOutOptions>` cannot set them.
- Load every Paymob secret from a secret store or environment variables, never from committed `appsettings.json`.
- Validate the HMAC of every cash-in callback with `IPaymobCashInBroker.Validate(...)` before you act on it. Pick the overload that matches what Paymob sends: `CashInCallbackTransaction` with the `hmac` query value, `CashInCallbackToken` with the `hmac` value, `CashInCallbackQueryParameters`, or a raw concatenated string with the `hmac` value.
- Trust only the signed callback fields. A valid HMAC covers the amount, currency, creation time, the transaction `Id`, the Paymob `Order.Id`, `IntegrationId`, `Owner`, the status flags (`Success`, `Pending`, `IsRefunded`, `IsVoided`, and the others), and the card source data. It does not cover `MerchantOrderId`, `Data.TxnResponseCode`, or `Data.Message`, and nothing stops a replay of an earlier valid callback. So:
  - Find your order by the signed `Order.Id`, which you store when you create the order, never by `MerchantOrderId`.
  - Check the signed `AmountCents` and `Currency` against the order.
  - Process callbacks idempotently, keyed on the transaction `Id`, so a replayed or repeated callback changes nothing.
  - Use the failure-reason helpers (`IsDeclinedError()`, `IsRiskChecksError()`, and the others) for display and logging only. They read unsigned fields.
- Pass cash-in amounts to the brokers in cents, as `long` (`CashInCreateIntentionRequest.Amount`, `CashInCreateOrderRequest.AmountCents`, the `amountCents` of `CashInPaymentKeyRequest`). Multiply a decimal EGP amount by 100 yourself. The `Headless.Payments.Paymob` services take decimal EGP and convert for you.
- Pass cash-out amounts as `decimal` EGP: `CashOutDisburseRequest` and `CashOutTransaction.Amount` use whole currency, not cents.
- Store every Paymob id as `long` (`long?` when nullable): transaction, order, integration, profile, owner, merchant, and gateway-integration ids, and kiosk bill references. They can exceed the `int` range.
- Build a cash-out request with a `CashOutDisburseRequest` factory (`Vodafone`, `Etisalat`, `Orange`, `BankWallet`, `BankCard`, `Accept`), not with its constructor. Each factory requires the fields its channel needs.
- Read a payout's outcome with `CashOutTransaction.IsSuccess()`, `IsFailed()`, and `IsPending()`, not by comparing status strings.
- Branch on the result's `Outcome`, never on `!IsSucceeded`. Every broker and service call returns a result with three outcomes, and a Paymob failure never throws. `IsSucceeded == false` covers both a rejection and an unknown outcome, and retrying an unknown payment, refund, void, or payout can move the money twice.
- After an `Unknown` money movement, resolve it by callback or inquiry before you send the request again. [Resolve an unknown outcome](#resolve-an-unknown-outcome) shows how.
- Expect exceptions only for your own mistakes and for cancellation: a `null` or empty argument throws `ArgumentException`, and a token cancelled before the request is sent throws `OperationCanceledException`. A token cancelled after the request was sent returns `Unknown`.
- Inject the brokers into scoped or transient services. `IPaymobCashInBroker` and `IPaymobCashOutBroker` are scoped.

---

## Core Concepts

### Cash-in and cash-out are separate APIs

Cash-in collects money from a customer. Cash-out sends money to a recipient. Paymob runs them as separate APIs with separate credentials, base URLs, and authentication.

### Cash-in flows

- **Payment intention (current).** `CreateIntentionAsync` uses `SecretKey` and returns a `ClientSecret` for the front end (Paymob.js). Use it for new integrations.
- **Order and payment key (legacy).** The `ApiKey` buys an auth token. You create an order, request a payment key, and send the user to the card iframe or a wallet deep link.
- **Channels.** Each payment method needs its own Paymob integration id from the merchant dashboard. Cards use an iframe. Wallets redirect to a deep link. Kiosk returns a bill reference the customer pays at a kiosk. Cash collection returns a reference for a courier network.

### Authentication tokens

- **Cash-in.** `IPaymobCashInAuthenticator` caches the auth token for `TokenRefreshBuffer` (default 55 minutes; Paymob tokens last 60 minutes) and fetches a new one after that.
- **Cash-out.** `IPaymobCashOutAuthenticator` uses the OAuth 2.0 password grant: `UserName` and `Password` in the body, `ClientId` and `ClientSecret` as Basic auth. It caches the bearer token for `TokenRefreshBuffer` (default 10 minutes).
- Both authenticators are singletons and drop the cached token when their options change.

### Outcomes

Every call returns a result with one of three outcomes. The brokers return `PaymobCashInResult<T>` and `PaymobCashOutResult<T>`; the services return `CashInResult<T>` and `CashOutResult<T>`.

| Outcome | What Paymob did | What it covers | What you do |
| --- | --- | --- | --- |
| `Succeeded` | Accepted the request. A pending payment or payout counts here; its final state arrives by callback. | A 2xx with a readable body that reports success or pending. | Use `Value` (broker) or `Data` (service). |
| `Rejected` | Definitely did not apply it. | A 4xx; a 2xx whose body reports a declined charge, refund, or void, or a failed payout; a request never sent because authentication failed or the circuit breaker is open. | Fix the cause, then send it again if you want. |
| `Unknown` | May have applied it. | A 5xx, a timeout, a dropped connection, cancellation after send, a 2xx with an unreadable body, or a status this client does not recognize. | Resolve it before any retry. |

The broker error (`PaymobCashInResultError`, `PaymobCashOutResultError`) carries `Message`, `StatusCode`, the response `Body`, and the `Exception` of a transport fault. `Message` never contains the body, so it is safe to log; the body can carry customer data. A rejected or unknown broker result keeps the parsed response in `Value` when Paymob answered with one, such as a declined transaction. `Match(succeeded, rejected, unknown)` makes you handle all three outcomes; `Map` projects a succeeded value and keeps any other outcome.

To fake a broker in a test, return `PaymobCashInResult.Succeeded(value)`, `Rejected<T>(error, value)`, or `Unknown<T>(error, value)`, and the matching `PaymobCashOutResult` factories.

### Resolve an unknown outcome

Paymob takes no idempotency key, so the only safe answer to `Unknown` is to find out what happened before you act again:

1. Store the reference you hold before the call: the Paymob order id for a cash-in charge, the transaction id for a refund or void, and your own record of the payout request for a cash-out.
2. Mark the operation as unresolved, not failed, and do not send it again.
3. Wait for the Paymob callback. Validate its HMAC, then match it to the stored reference: `Order.Id` for a charge, the transaction `Id` for a refund or void, `transaction_id` for a payout.
4. If no callback arrives within your deadline, query Paymob:
   - Cash-in charge: `IPaymobCashInBroker.GetOrderAsync(orderId)`, or `GetOrdersPageAsync(CashInOrdersPageRequest.Create.WithMerchantOrderId(id))` when you set a merchant order id. The service result carries the order in `CashInResult<T>.OrderId`.
   - Refund or void: `GetTransactionAsync(transactionId)`, then read `IsRefunded` or `IsVoided`.
   - Payout: `IPaymobCashOutBroker.GetTransactionsAsync([transactionId], isBankTransactions)` when the unknown result's `Value` holds the transaction. A payout that got no response at all has no transaction id yet; resolve it by its callback.
5. Send the request again only when the inquiry shows it did not apply. An inquiry that itself returns `Unknown` resolves nothing: try it again later.

```csharp
public sealed class RefundHandler(IPaymobCashInService cashIn, IPaymobCashInBroker broker)
{
    public async Task<bool> RefundAsync(string transactionId, decimal amount, CancellationToken ct)
    {
        var result = await cashIn.RefundAsync(new PaymobRefundRequest(transactionId, amount), ct);

        switch (result.Outcome)
        {
            case PaymobCashInOutcome.Succeeded:
                return true;
            case PaymobCashInOutcome.Rejected:
                return false; // result.Error.Code says why; nothing was refunded
            default:
                // The refund may have gone through. Look it up instead of refunding again.
                var lookup = await broker.GetTransactionAsync(transactionId, ct);

                return lookup.IsSucceeded && lookup.Value.IsRefunded;
        }
    }
}
```

**Verify:** in a test, make the broker return `PaymobCashInResult.Unknown<CashInCallbackTransaction>(new PaymobCashInResultError("timeout"))` from `RefundTransactionAsync` and assert that your code calls `GetTransactionAsync` and does not call `RefundTransactionAsync` a second time.

### No automatic retry for writes

Paymob takes no idempotency key on its write calls, so a retried POST can create a duplicate order, refund twice, or pay out twice. Both broker clients therefore never retry a POST automatically: orders, payment keys, intentions, refunds, voids, and payouts. GET inquiries keep the standard retry. The rule holds in a host that adds a resilience handler to every client, such as `AddHeadless()` service defaults, because the broker removes that handler from its own client.

`merchant_order_id` and `special_reference` are fields you choose. Paymob does not use them to deduplicate a retry.

The broker clients are named `Headless:PaymobCashIn` and `Headless:PaymobCashOut`. `configureClient` takes only an `Action<HttpClient>`. To add a proxy or a `DelegatingHandler`, configure the named client: `services.AddHttpClient("Headless:PaymobCashIn").ConfigurePrimaryHttpMessageHandler(...)`.

To change timeouts, pass `configureResilience` to `AddPaymobCashIn` or `AddPaymobCashOut`. It runs after the defaults. Setting `Retry.ShouldHandle` there turns POST retries back on; do it only if you accept duplicates.

---

## Headless.Payments.Paymob.CashIn

The Paymob Accept cash-in broker.

### Setup

```bash
dotnet add package Headless.Payments.Paymob.CashIn
```

`AddPaymobCashIn` takes `IConfiguration`, `Action<PaymobCashInOptions>`, or `Action<PaymobCashInOptions, IServiceProvider>`, then optional `configureClient` and `configureResilience`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymobCashIn(options =>
{
    options.ApiKey = builder.Configuration["Paymob:ApiKey"]!;
    options.Hmac = builder.Configuration["Paymob:Hmac"]!;
    options.SecretKey = builder.Configuration["Paymob:SecretKey"]!;
});

// Or: builder.Services.AddPaymobCashIn(builder.Configuration.GetSection("Paymob:CashIn"));
```

Create a payment intention (preferred), or a legacy payment key:

```csharp
public sealed class PaymentService(IPaymobCashInBroker broker)
{
    public async Task<string> CreatePaymentAsync(long amountCents, long integrationId, CancellationToken ct)
    {
        var response = await broker.CreateIntentionAsync(
            new CashInCreateIntentionRequest
            {
                Amount = amountCents, // in cents: 10000 = 100 EGP
                Currency = "EGP",
                PaymentMethods = [integrationId],
                BillingData = new CashInCreateIntentionRequestBillingData
                {
                    FirstName = "Ahmed",
                    LastName = "Ali",
                    PhoneNumber = "+201001234567",
                    Email = "ahmed@example.com",
                },
                Items = [],
            },
            ct
        );

        // An intention moves no money, so an unknown outcome leaves at most an unused intention behind.
        return response.IsSucceeded
            ? response.Value.ClientSecret // pass to Paymob.js in the front end
            : throw new InvalidOperationException(response.Error.Message);
    }

    public Task<PaymobCashInResult<CashInPaymentKeyResponse>> CreateLegacyPaymentKeyAsync(
        long integrationId,
        long orderId,
        long amountCents,
        CancellationToken ct
    )
    {
        var billingData = new CashInBillingData("Ahmed", "Ali", "+201001234567", "ahmed@example.com")
        {
            Country = "EG",
            City = "Cairo",
            Street = "Tahrir Street",
        };

        return broker.RequestPaymentKeyAsync(
            new CashInPaymentKeyRequest(integrationId, orderId, billingData, amountCents),
            ct
        );
    }
}
```

Validate a transaction callback:

```csharp
[ApiController]
public sealed class PaymobCallbackController(IPaymobCashInBroker broker) : ControllerBase
{
    [HttpPost("paymob/callback")]
    public IActionResult HandleCallback([FromBody] CashInCallbackTransaction transaction, [FromQuery] string hmac)
    {
        if (!broker.Validate(transaction, hmac))
        {
            return BadRequest("Invalid HMAC");
        }

        if (transaction.Success)
        {
            // Mark the order whose Paymob id is transaction.Order.Id as paid, once per transaction.Id,
            // after checking transaction.AmountCents and transaction.Currency against it.
        }

        return Ok();
    }
}
```

```json
{
  "Paymob": {
    "CashIn": {
      "ApiKey": "your-api-key",
      "Hmac": "your-hmac-secret",
      "SecretKey": "your-secret-key"
    }
  }
}
```

**Verify:** start the host; invalid options fail the start with `OptionsValidationException`. In a test, compute the expected HMAC as the lowercase hex HMAC-SHA512 of `transaction.ToConcatenatedString()`, keyed with your `Hmac` secret. Assert that `broker.Validate(transaction, hmac)` returns `true`, and `false` after you change a signed field such as `AmountCents`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `ApiKey` | Yes | | Merchant API key for the legacy auth token. |
| `Hmac` | Yes | | Secret that verifies callback HMACs. |
| `SecretKey` | Yes | | Secret key for the intention API. |
| `ExpirationPeriod` | No | `3600` | Payment key lifetime, in seconds. Greater than 60. |
| `TokenRefreshBuffer` | No | `00:55:00` | How long the auth token is cached. Greater than zero and under 60 minutes. |
| `ApiBaseUrl` | No | `https://accept.paymobsolutions.com/api` | Legacy API base URL. |
| `CreateIntentionUrl` | No | `https://accept.paymob.com/v1/intention/` | Intention endpoint. |
| `RefundUrl` | No | `https://accept.paymob.com/api/acceptance/void_refund/refund` | Refund endpoint. |
| `VoidRefundUrl` | No | `https://accept.paymob.com/api/acceptance/void_refund/void` | Void endpoint. |
| `IframeBaseUrl` | No | `https://accept.paymob.com/api/acceptance/iframes` | Card iframe base URL. |

Every URL must be HTTPS, or HTTP on loopback for a test server, with no user info.

### Design and runtime behavior

- **Broker calls.** `IPaymobCashInBroker` has:
  - `CreateIntentionAsync`: the intention API; returns the `ClientSecret`.
  - `CreateOrderAsync` and `RequestPaymentKeyAsync`: the legacy flow.
  - `CreateWalletPayAsync`, `CreateKioskPayAsync`, `CreateCashCollectionPayAsync`, and `CreateSavedTokenPayAsync`: pay with a payment key on one channel.
  - `RefundTransactionAsync` and `VoidTransactionAsync`.
  - `GetTransactionAsync`, `GetTransactionsPageAsync`, `GetOrderAsync`, and `GetOrdersPageAsync`.
  - `Validate(...)` (four overloads) and `CreateIframeSrc(iframeId, token)`, which builds the card iframe URL.
- **Outcomes.** Every call returns `PaymobCashInResult<T>` ([Outcomes](#outcomes)). A charge, refund, or void that Paymob answers with `success` and `pending` both `false` is `Rejected`, with the transaction in `Value`. A pending one is `Succeeded`. An authentication failure returns `Rejected` without sending the call. `IPaymobCashInAuthenticator.GetAuthenticationTokenAsync` returns a result too, and caches the token only on success.
- **Constants.** `CashInCallbackTypes` holds `TRANSACTION` and `TOKEN`. `CashInStatuses` holds `pending`, `declined`, and `success`.
- **Billing data.** `CashInBillingData` takes first name, last name, phone number, and email in its constructor. Its optional address and shipping properties default to `"NA"`.
- **Secrets in logs.** `PaymobCashInOptions.ToString()` prints `ApiKey`, `Hmac`, and `SecretKey` as `***`.

---

## Headless.Payments.Paymob.CashOut

The Paymob disbursement (cash-out) broker.

### Setup

```bash
dotnet add package Headless.Payments.Paymob.CashOut
```

```csharp
var builder = WebApplication.CreateBuilder(args);

// The credentials are init-only, so bind them from configuration.
builder.Services.AddPaymobCashOut(builder.Configuration.GetSection("Paymob:CashOut"));
```

```json
{
  "Paymob": {
    "CashOut": {
      "ApiBaseUrl": "https://disbursement.paymob.com/api/",
      "UserName": "your-username",
      "Password": "your-password",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret"
    }
  }
}
```

Pay out to a mobile wallet:

```csharp
public sealed class DisbursementService(IPaymobCashOutBroker broker)
{
    public async Task<PayoutState> DisburseToVodafoneAsync(decimal amount, string phoneNumber, CancellationToken ct)
    {
        var request = CashOutDisburseRequest.Vodafone(amount, phoneNumber);
        var result = await broker.DisburseAsync(request, ct);

        return result.Outcome switch
        {
            PaymobCashOutOutcome.Succeeded when result.Value!.IsPending() => PayoutState.Pending,
            PaymobCashOutOutcome.Succeeded => PayoutState.Paid,
            // A 4xx, or Paymob reported the payout failed; result.Value holds the failed transaction when it answered.
            PaymobCashOutOutcome.Rejected => PayoutState.Failed,
            // The payout may have gone out. Wait for its callback; never send it again blind.
            _ => PayoutState.Unresolved,
        };
    }
}

public enum PayoutState
{
    Paid,
    Pending,
    Failed,
    Unresolved,
}
```

Pay out to a bank account:

```csharp
var request = CashOutDisburseRequest.BankCard(
    amount: 500m,
    cardNumber: "1234567890123456", // IBAN or card number
    bankCode: "CIB",
    transactionType: BankTransactionTypes.CashTransfer,
    fullName: "Ahmed Ali"
);
var result = await broker.DisburseAsync(request, cancellationToken);
```

**Verify:** start the host; a missing credential or a non-HTTPS `ApiBaseUrl` fails the start with `OptionsValidationException`. Then call `GetBudgetAsync()`: it proves the credentials without moving money. A bad credential returns a rejected result whose `Error.StatusCode` is `401`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `ApiBaseUrl` | Yes | | Cash-out API base URL. HTTPS, or HTTP on loopback for a test server, with no user info. |
| `UserName` | Yes | | OAuth password-grant user name. |
| `Password` | Yes | | OAuth password-grant password. |
| `ClientId` | Yes | | OAuth client id, sent as Basic auth. |
| `ClientSecret` | Yes | | OAuth client secret, sent as Basic auth. |
| `TokenRefreshBuffer` | No | `00:10:00` | How long the bearer token is cached. Greater than zero and at most 60 minutes. |

### Design and runtime behavior

- **Broker calls.** `IPaymobCashOutBroker` has:
  - `DisburseAsync(request)`: pays out. The result's `Value` is the `CashOutTransaction`.
  - `GetBudgetAsync()`: returns `CashOutBudgetResponse`. Paymob reports the balance as a sentence in `CurrentBudget`, such as `"Your current budget is 888.25 LE"`.
  - `GetTransactionsAsync(transactionsIds, isBankTransactions, page)`: a paginated inquiry; returns `CashOutGetTransactionsResponse` (`Count`, `Next`, `Previous`, `Results`).
  - Paymob limits `GetBudgetAsync` and `GetTransactionsAsync` to 5 requests per minute.
- **Channels.** `CashOutDisburseRequest` factories:
  - `Vodafone(amount, phoneNumber)` and `Etisalat(amount, phoneNumber)`.
  - `Orange(amount, phoneNumber, fullName)` and `BankWallet(amount, phoneNumber, fullName)`.
  - `BankCard(amount, cardNumber, bankCode, transactionType, fullName)`, with `transactionType` from `BankTransactionTypes` (`CashTransfer`, `CreditCard`, `PrepaidCard`, `Salary`).
  - `Accept(amount, phoneNumber, firstName, lastName, email)`: an Aman kiosk payout.
- **Outcome helpers.** `CashOutTransaction` has `IsSuccess()`, `IsFailed()`, `IsPending()`, `IsProviderDownError()`, `IsAuthenticationError()`, `IsNotHaveVodafoneCashError()`, `IsNotHaveEtisalatCashError()`, and `IsRequestValidationError()`.
- **Outcomes.** Every call returns `PaymobCashOutResult<T>` ([Outcomes](#outcomes)). `DisburseAsync` is `Succeeded` for a successful or pending payout, `Rejected` for a 4xx or a `failed` disbursement status (the transaction is in `Value`), and `Unknown` for anything else, including a status this client does not recognize. An authentication failure returns `Rejected` without sending the payout.
- **Endpoint.** Every request goes to an absolute URL built from the current `ApiBaseUrl`. Setting the `HttpClient` base address does not change the endpoint.

---

## Headless.Payments.Paymob

Typed cash-in and cash-out services over the two brokers, with fee calculation and error mapping.

### Setup

```bash
dotnet add package Headless.Payments.Paymob
```

Register the brokers first, then the services:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymobCashIn(builder.Configuration.GetSection("Paymob:CashIn"));
builder.Services.AddPaymobCashOut(builder.Configuration.GetSection("Paymob:CashOut"));

// IPaymobCashInService, ICashOutService, IPaymobCashInFeesCalculator
builder.Services.AddPaymobServices();
```

`AddPaymobServices` registers the fee calculator with Paymob's default fees: 6 EGP per transaction, 2.5%, and 14% VAT on the fee. To use your merchant's rates, register your own calculator before the call. The services are added only when not already registered, so yours is kept:

```csharp
builder.Services.AddSingleton<IPaymobCashInFeesCalculator>(
    new PaymobCashInFeesCalculator(
        fixedFeesPerTransaction: 6m,
        percentageFeesPerTransaction: 0.025m,
        vatPercentOnFees: 0.14m
    )
);
builder.Services.AddPaymobServices();
```

Start a card payment (legacy iframe flow):

```csharp
public sealed class CheckoutService(IPaymobCashInService cashIn)
{
    public async Task<string> GetIframeUrlAsync(
        decimal amount,
        PaymobCashInCustomerData customer,
        long cardIntegrationId,
        string iframeId,
        CancellationToken ct
    )
    {
        var response = await cashIn.StartAsync(
            new PaymobCardCashInRequest(
                Amount: amount,
                Customer: customer,
                CardIntegrationId: cardIntegrationId,
                IframeSrc: iframeId
            ),
            ct
        );
        // Card checkout moves no money until the customer pays in the iframe.
        return response.IsSucceeded ? response.Data.IframeSrc : throw new InvalidOperationException(response.Error.Code);
    }
}
```

Pay out to a bank account:

```csharp
public sealed class PayoutService(ICashOutService cashOut)
{
    public async Task PayoutAsync(decimal amount, string accountNumber, string bankCode, CancellationToken ct)
    {
        var result = await cashOut.DisburseAsync(
            new BankAccountCashOutRequest(
                Amount: amount,
                AccountNumber: accountNumber,
                BankCode: bankCode,
                Type: BankTransactionType.CashTransfer,
                FullName: "Ahmed Ali"
            ),
            ct
        );

        if (result.IsUnknown)
        {
            // The payout may have gone out: record it as unresolved and wait for its callback.
            return;
        }

        if (result.IsRejected)
        {
            throw new InvalidOperationException(result.Error.Description);
        }
    }
}
```

**Verify:** without the broker registrations, resolving `IPaymobCashInService` or `ICashOutService` fails, because `AddPaymobServices` does not register the brokers. In a test, resolve both services from a scope and assert that `IPaymobCashInFeesCalculator.CalculateDeductFees(100m)` matches your merchant's fee for 100 EGP.

### Configuration

No options of its own. `PaymobCashInFeesCalculator` takes the fee rates in its constructor: pass the values Paymob set for your merchant account.

### Design and runtime behavior

- **Cash-in service.** `IPaymobCashInService` has:
  - Every method returns `CashInResult<T>`, whose `Data` is the response below on success.
  - `StartAsync(PaymobCardCashInRequest)` returns `PaymobCardCashInResponse` (`IframeSrc`, `PaymentKey`, `OrderId`, `Expiration`).
  - `StartAsync(PaymobWalletCashInRequest)` returns `PaymobWalletCashInResponse` (`RedirectUrl`, `OrderId`, `Expiration`).
  - `StartAsync(PaymobKioskCashInRequest)` returns `PaymobKioskCashInResponse` (`BillingReference`, `OrderId`, `Expiration`).
  - `StartAsync(PaymobCardSavedTokenCashInRequest)` returns `PaymobCardSavedTokenCashInResponse`.
  - `StartAsync(CashInCreateIntentionRequest)` passes through to the broker.
  - `RefundAsync(PaymobRefundRequest)` and `VoidAsync(PaymobVoidRequest)`.
- **Cash-in result.** `CashInResult<T>` has `Outcome`, `IsSucceeded`, `IsRejected`, `IsUnknown`, `Data`, `Error` (an `ErrorDescriptor`), and `OrderId`. A failure while creating the order or the payment key is `Rejected` with `cash_in:provider_connection_failed`: no money moved, so start again with a new merchant order id. Once the pay call is sent, a declined payment is `Rejected` with `cash_in:declined`, and an unknown one is `Unknown` with `cash_in:outcome_unknown` and the Paymob order in `OrderId`. A wallet or kiosk payment that Paymob accepted without a redirect URL or a bill reference is also `Unknown`, because the payment exists.
- **Amounts.** The service takes decimal EGP and converts to cents. It rounds a fraction of a cent up on a charge and down on a refund, so partial refunds of one charge never add up to more than it: three refunds of `100m / 3` ask for 9,999 cents of a 10,000-cent charge. As a result, a full refund of a charge with a fraction of a cent, such as `10.005m`, returns one cent less than was charged (1,000 of 1,001 cents). Charge whole cents to avoid it.
- **Invalid amounts.** A zero or negative charge or refund, or a refund under one cent, throws `ArgumentOutOfRangeException` before any call to Paymob.
- **Cash-out service.** `ICashOutService.DisburseAsync` takes `VodafoneCashOutRequest`, `EtisalatCashOutRequest`, `OrangeCashOutRequest`, `BankWalletCashOutRequest`, `BankAccountCashOutRequest`, or `KioskCashOutRequest`, and returns `CashOutResult<T>`. A kiosk payout's data includes the `BillingReference`.
- **Cash-out result.** `CashOutResult<T>` has `Outcome`, `IsSucceeded`, `IsRejected`, `IsUnknown`, `Data` (set on success), `Error` (an `ErrorDescriptor`, set otherwise), and `Response` (the raw JSON, when Paymob answered). A pending payout counts as `Succeeded`. An unknown payout has the code `cash_out:outcome_unknown`; a kiosk payout accepted without a billing reference is also `Unknown`.
- **Fees.** `IPaymobCashInFeesCalculator` does arithmetic only, with no network call:
  - `CalculateDeductFees(amount)`: the total fee Paymob deducts.
  - `CalculateDeductFeesAndTax(amount)`: the fee and the VAT on it.
  - `AddFeesForNet(net)`: the gross amount to charge so you receive at least `net`. It rounds up to a whole unit, so it can over-cover by up to 1.
  - `CalcFeesForNet(net)`: the fee part of that gross amount.
  - A zero or negative amount throws `ArgumentOutOfRangeException`.
- **Response codes.** `PaymobTransactionResponseCodes` holds card response codes (`0` is approved). `PaymobRiskDeclineCodes` holds Paymob fraud-check decline codes (111 to 301).
- **Customer data in logs.** `PaymobCashInCustomerData.ToString()` redacts every field, so a logged request never carries customer PII.
- **Lifetimes.** `IPaymobCashInService` and `ICashOutService` are scoped. `IPaymobCashInFeesCalculator` is a singleton.
