## Release 0.12.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HF001   | Headless.Jobs.SourceGenerator | Error | Job class should be public or internal
HF002   | Headless.Jobs.SourceGenerator | Error | Job method should be public or internal
HF003   | Headless.Jobs.SourceGenerator | Error | Invalid cron expression
HF004   | Headless.Jobs.SourceGenerator | Error | Invalid job identity
HF005   | Headless.Jobs.SourceGenerator | Error | Duplicate job identity
HF006   | Headless.Jobs.SourceGenerator | Warning | Multiple constructors on a job class
HF007   | Headless.Jobs.SourceGenerator | Error | Abstract or generic job class
HF008   | Headless.Jobs.SourceGenerator | Error | Nested job class
HF009   | Headless.Jobs.SourceGenerator | Error | Job class does not implement IJob or IJob<TArgs>
HF010   | Headless.Jobs.SourceGenerator | Error | Multiple constructors marked as the job constructor
HF011   | Headless.Jobs.SourceGenerator | Error | Duplicate job argument type
HF012   | Headless.Jobs.SourceGenerator | Error | Undefined JobPriority value on [Job]
HF013   | Headless.Jobs.SourceGenerator | Error | Negative maximum concurrency on [Job]
HF014   | Headless.Jobs.SourceGenerator | Error | Unknown Jobs middleware target identity
HF015   | Headless.Jobs.SourceGenerator | Error | Duplicate Jobs middleware declaration
HF016   | Headless.Jobs.SourceGenerator | Error | Class-level Jobs middleware is not on a [Job] class
HF017   | Headless.Jobs.SourceGenerator | Error | Class-level Jobs middleware redundantly specifies Function
HF018   | Headless.Jobs.SourceGenerator | Error | Assembly-level Function targets a job declared in the same assembly
HF019   | Headless.Jobs.SourceGenerator | Error | Middleware type is inaccessible to generated registration code
HF020   | Headless.Jobs.SourceGenerator | Error | Undefined missed-run policy on [Job]
HF021   | Headless.Jobs.SourceGenerator | Error | Non-positive missed-run grace on [Job]
