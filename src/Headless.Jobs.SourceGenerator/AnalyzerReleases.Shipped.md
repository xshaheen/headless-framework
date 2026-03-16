## Release 2.5.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TQ001   | Headless.Jobs.SourceGenerator | Error | Class should be public or internal to be used with [JobFunction]
TQ002   | Headless.Jobs.SourceGenerator | Error | Method should be public or internal to be used with [JobFunction]
TQ003   | Headless.Jobs.SourceGenerator | Error | Invalid cron expression
TQ004   | Headless.Jobs.SourceGenerator | Error | Missing function name in [JobFunction] attribute
TQ005   | Headless.Jobs.SourceGenerator | Error | Duplicate function name across [JobFunction] methods
TQ006   | Headless.Jobs.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [JobsConstructor] attribute is specified
TQ007   | Headless.Jobs.SourceGenerator | Error | Abstract class contains [JobFunction] methods
TQ008   | Headless.Jobs.SourceGenerator | Error | Nested class contains [JobFunction] methods - only allowed in top-level classes
TQ009   | Headless.Jobs.SourceGenerator | Error | Invalid JobFunction parameter - only JobFunctionContext, JobFunctionContext<T>, CancellationToken, or no parameters allowed
TQ010   | Headless.Jobs.SourceGenerator | Error | Multiple constructors with [JobsConstructor] attribute - only one constructor can be marked