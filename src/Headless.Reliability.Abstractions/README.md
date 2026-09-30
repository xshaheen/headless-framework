# Headless.Reliability.Abstractions

Defines the reliability contracts that Headless Messaging and Jobs share, starting with `IFailurePolicy`.

## Why use this package

Lets a message consumer or a job name its failure policy as a type in its declaration, so a policy and the handlers it governs live together and a misspelled policy fails the build. Messaging and Jobs reference this package, so policy types written against it work with both.

## Install

```bash
dotnet add package Headless.Reliability.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Reliability guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/reliability.md#headlessreliabilityabstractions)
