## Release 2.5.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TQ001   | Headless.Ticker.SourceGenerator | Error | Class should be public or internal to be used with [TickerFunction]
TQ002   | Headless.Ticker.SourceGenerator | Error | Method should be public or internal to be used with [TickerFunction]
TQ003   | Headless.Ticker.SourceGenerator | Error | Invalid cron expression
TQ004   | Headless.Ticker.SourceGenerator | Error | Missing function name in [TickerFunction] attribute
TQ005   | Headless.Ticker.SourceGenerator | Error | Duplicate function name across [TickerFunction] methods
TQ006   | Headless.Ticker.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [TickerQConstructor] attribute is specified
TQ007   | Headless.Ticker.SourceGenerator | Error | Abstract class contains [TickerFunction] methods
TQ008   | Headless.Ticker.SourceGenerator | Error | Nested class contains [TickerFunction] methods - only allowed in top-level classes
TQ009   | Headless.Ticker.SourceGenerator | Error | Invalid TickerFunction parameter - only TickerFunctionContext, TickerFunctionContext<T>, CancellationToken, or no parameters allowed
TQ010   | Headless.Ticker.SourceGenerator | Error | Multiple constructors with [TickerQConstructor] attribute - only one constructor can be marked