# Framework.Specifications

A lightweight and flexible implementation of the **Specification Pattern** for .NET. This package allows you to encapsulate business logic and domain rules into reusable, combinable specifications. These specifications can be used for validation, in-memory filtering, or database query filtering (via LINQ expressions).

## Features

-   **Generic Implementation**: Works with any type `T` via `ISpecification<T>`.
-   **Expression Support**: Built on top of `System.Linq.Expressions`, making it compatible with ORMs (like Entity Framework) for efficient database queries.
-   **Composability**: Easily combine specifications using logical operators:
    -   `And`
    -   `Or`
    -   `Not`
    -   `AndNot`
    -   `OrNot`
-   **Implicit Conversions**: Seamlessly convert between `Specification<T>` and `Expression<Func<T, bool>>`.

## Installation

This package is part of the `Framework` libraries. Ensure you have the necessary references in your project.

## Usage

### 1. Defining a Specification

You can define a reusable specification by inheriting from `Specification<T>` and implementing the `ToExpression` method.

```csharp
using Framework.Specifications;
using System.Linq.Expressions;

public class CustomerIsActiveSpecification : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression()
    {
        return customer => customer.IsActive;
    }
}

public class CustomerHasBalanceSpecification : Specification<Customer>
{
    private readonly decimal _minBalance;

    public CustomerHasBalanceSpecification(decimal minBalance)
    {
        _minBalance = minBalance;
    }

    public override Expression<Func<Customer, bool>> ToExpression()
    {
        return customer => customer.Balance >= _minBalance;
    }
}
```

### 2. Using Specifications

You can check if an entity satisfies a specification using `IsSatisfiedBy` or use it directly in LINQ queries.

```csharp
var activeSpec = new CustomerIsActiveSpecification();
var richSpec = new CustomerHasBalanceSpecification(1000);

var customer = new Customer { IsActive = true, Balance = 500 };

// Check in-memory
bool isActive = activeSpec.IsSatisfiedBy(customer); // true
bool isRich = richSpec.IsSatisfiedBy(customer);     // false
```

### 3. Combining Specifications

Use extension methods to chain complex rules.

```csharp
// Combine specifications
var richAndActiveSpec = activeSpec.And(richSpec);

// Using implicit operators / ad-hoc expressions
Specification<Customer> isVipSpec = richAndActiveSpec.Or(c => c.IsVip);

if (isVipSpec.IsSatisfiedBy(customer))
{
    // ...
}
```

### 4. Using with ORMs (LINQ)

Since specifications return `Expression<Func<T, bool>>`, they can be passed directly to `Where` clauses in IQueryables (e.g., Entity Framework Core).

```csharp
public List<Customer> GetVipCustomers()
{
    var spec = new CustomerIsActiveSpecification()
               .And(new CustomerHasBalanceSpecification(10000));

    // Automatically converted to an Expression tree
    return _dbContext.Customers.Where(spec).ToList();
}
```

### 5. Ad-Hoc Specifications

You don't always need to create a class. You can create a specification directly from an expression.

```csharp
var spec = Specification<Customer>.FromExpression(c => c.Age > 18);
```

## Core Components

-   **`ISpecification<T>`**: The core interface defining `IsSatisfiedBy` and `ToExpression`.
-   **`Specification<T>`**: The base abstract class.
-   **`ExpressionSpecification<T>`**: a wrapper for generic lambda expressions.
-   **`SpecificationExtensions`**: Provides the fluent API for `And`, `Or`, `Not`, etc.
