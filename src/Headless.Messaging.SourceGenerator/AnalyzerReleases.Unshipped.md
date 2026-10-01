### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HM001   | Headless.Messaging.SourceGenerator | Error | Invalid consumer identity
HM002   | Headless.Messaging.SourceGenerator | Error | Duplicate consumer identity
HM003   | Headless.Messaging.SourceGenerator | Error | Consumer class does not implement IConsume<T>
HM004   | Headless.Messaging.SourceGenerator | Error | Second Queue consumer for one message
HM006   | Headless.Messaging.SourceGenerator | Warning | IOnSubscriptionEstablished on a consumer that is not every-instance
HM007   | Headless.Messaging.SourceGenerator | Error | Consumer class or message type is inaccessible to generated code
HM008   | Headless.Messaging.SourceGenerator | Error | Abstract or generic consumer class
HM009   | Headless.Messaging.SourceGenerator | Error | Consumer class declares both lane attributes
