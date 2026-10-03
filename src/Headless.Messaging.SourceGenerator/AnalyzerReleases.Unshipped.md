### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HM001   | Headless.Messaging.SourceGenerator | Error | Invalid consumer identity
HM002   | Headless.Messaging.SourceGenerator | Error | Duplicate consumer identity
HM003   | Headless.Messaging.SourceGenerator | Error | Consumer class implements neither IConsume<T> nor IRespond<TRequest, TResponse>
HM004   | Headless.Messaging.SourceGenerator | Error | Second Queue consumer for one message
HM005   | Headless.Messaging.SourceGenerator | Error | Failure policy type cannot be constructed by generated code
HM006   | Headless.Messaging.SourceGenerator | Warning | IOnSubscriptionEstablished on a consumer that is not every-instance
HM007   | Headless.Messaging.SourceGenerator | Error | Consumer class or message type is inaccessible to generated code
HM008   | Headless.Messaging.SourceGenerator | Error | Abstract or generic consumer class
HM009   | Headless.Messaging.SourceGenerator | Error | Consumer class declares both lane attributes
HM010   | Headless.Messaging.SourceGenerator | Error | Failure policy on an every-instance consumer
HM011   | Headless.Messaging.SourceGenerator | Error | Responder on the Bus lane
HM012   | Headless.Messaging.SourceGenerator | Error | Consumer and responder for one message
HM013   | Headless.Messaging.SourceGenerator | Error | One request answered with several response types
HM014   | Headless.Messaging.SourceGenerator | Warning | Request expects a response type its responder does not answer with
