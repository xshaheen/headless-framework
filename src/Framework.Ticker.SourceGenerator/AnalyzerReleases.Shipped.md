## Release 2.5.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TQ001   | Framework.Ticker.SourceGenerator | Error | Class should be public or internal to be used with [TickerFunction]
TQ002   | Framework.Ticker.SourceGenerator | Error | Method should be public or internal to be used with [TickerFunction]
TQ003   | Framework.Ticker.SourceGenerator | Error | Invalid cron expression
TQ004   | Framework.Ticker.SourceGenerator | Error | Missing function name in [TickerFunction] attribute
TQ005   | Framework.Ticker.SourceGenerator | Error | Duplicate function name across [TickerFunction] methods
TQ006   | Framework.Ticker.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [TickerQConstructor] attribute is specified
TQ007   | Framework.Ticker.SourceGenerator | Error | Abstract class contains [TickerFunction] methods
TQ008   | Framework.Ticker.SourceGenerator | Error | Nested class contains [TickerFunction] methods - only allowed in top-level classes
TQ009   | Framework.Ticker.SourceGenerator | Error | Invalid TickerFunction parameter - only TickerFunctionContext, TickerFunctionContext<T>, CancellationToken, or no parameters allowed
TQ010   | Framework.Ticker.SourceGenerator | Error | Multiple constructors with [TickerQConstructor] attribute - only one constructor can be marked