### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HF001   | Headless.Jobs.SourceGenerator | Error | Class should be public or internal to be used with [JobFunction]
HF002   | Headless.Jobs.SourceGenerator | Error | Method should be public or internal to be used with [JobFunction]
HF003   | Headless.Jobs.SourceGenerator | Error | Invalid cron expression
HF004   | Headless.Jobs.SourceGenerator | Error | Missing function name in [JobFunction] attribute
HF005   | Headless.Jobs.SourceGenerator | Error | Duplicate function name across [JobFunction] methods
HF006   | Headless.Jobs.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [JobsConstructor] attribute is specified
HF007   | Headless.Jobs.SourceGenerator | Error | Abstract class contains [JobFunction] methods
HF008   | Headless.Jobs.SourceGenerator | Error | Nested class contains [JobFunction] methods - only allowed in top-level classes
HF009   | Headless.Jobs.SourceGenerator | Error | Invalid JobFunction parameter - only JobFunctionContext, JobFunctionContext<T>, CancellationToken, or no parameters allowed
HF010   | Headless.Jobs.SourceGenerator | Error | Multiple constructors with [JobsConstructor] attribute - only one constructor can be marked
HF011   | Headless.Jobs.SourceGenerator | Error | Duplicate request type across [JobFunction] methods
HF012   | Headless.Jobs.SourceGenerator | Error | Undefined JobPriority value on [JobFunction]
HF013   | Headless.Jobs.SourceGenerator | Error | Negative maximum concurrency on [JobFunction]
HF014   | Headless.Jobs.SourceGenerator | Error | Unknown Jobs middleware target descriptor
HF015   | Headless.Jobs.SourceGenerator | Error | Duplicate Jobs middleware declaration
HF016   | Headless.Jobs.SourceGenerator | Error | Method-level Jobs middleware is not beside a JobFunction declaration
HF017   | Headless.Jobs.SourceGenerator | Error | Method-level Jobs middleware redundantly specifies Function
HF018   | Headless.Jobs.SourceGenerator | Error | Assembly-level Function targets a function declared in the same assembly
HF019   | Headless.Jobs.SourceGenerator | Error | Middleware type is inaccessible to generated registration code

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TQ001   | Headless.Jobs.SourceGenerator | Error | Renamed to HF001
TQ002   | Headless.Jobs.SourceGenerator | Error | Renamed to HF002
TQ003   | Headless.Jobs.SourceGenerator | Error | Renamed to HF003
TQ004   | Headless.Jobs.SourceGenerator | Error | Renamed to HF004
TQ005   | Headless.Jobs.SourceGenerator | Error | Renamed to HF005
TQ006   | Headless.Jobs.SourceGenerator | Warning | Renamed to HF006
TQ007   | Headless.Jobs.SourceGenerator | Error | Renamed to HF007
TQ008   | Headless.Jobs.SourceGenerator | Error | Renamed to HF008
TQ009   | Headless.Jobs.SourceGenerator | Error | Renamed to HF009
TQ010   | Headless.Jobs.SourceGenerator | Error | Renamed to HF010
