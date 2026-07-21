// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>A month bucket returned by <c>CountPerMonthAsync</c> with a <c>DateOnly</c> date selector.</summary>
public sealed record EntityPerDateOnly(DateOnly Date, int Count);

/// <summary>A month bucket returned by <c>CountPerMonthAsync</c> with a <c>DateTimeOffset</c> date selector.</summary>
public sealed record EntityPerDateTimeOffset(DateTimeOffset Date, int Count);
