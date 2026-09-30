### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HF022   | Headless.Jobs.SourceGenerator | Error | Undefined overlap policy on [Job]
HF023   | Headless.Jobs.SourceGenerator | Error | Failure policy type on [Job] does not implement IFailurePolicy

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HF002   | Headless.Jobs.SourceGenerator | Error | Method accessibility no longer applies: a job is a class, not a method
HF006   | Headless.Jobs.SourceGenerator | Warning | Job classes are constructed through dependency injection, which selects the constructor
HF010   | Headless.Jobs.SourceGenerator | Error | [JobsConstructor] is removed; mark a constructor with [ActivatorUtilitiesConstructor]
