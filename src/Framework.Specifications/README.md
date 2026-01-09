# Framework.Specifications

Implementation of the Specification pattern for composable business rules.

## Problem Solved

Provides a type-safe, composable specification pattern implementation enabling complex business rules to be expressed as reusable, testable objects that can be combined using logical operators and used with LINQ/EF Core.

## Key Features

- `ISpecification<T>` - Base specification interface
- `Specification<T>` - Abstract base class with expression support
- Composite specifications:
  - `AndSpecification<T>` - Logical AND
  - `OrSpecification<T>` - Logical OR
  - `NotSpecification<T>` - Logical NOT
  - `AndNotSpecification<T>` - Logical AND NOT
  - `OrNotSpecification<T>` - Logical OR NOT
- `AnySpecification<T>` - Always returns true
- `NoneSpecification<T>` - Always returns false
- `ExpressionSpecification<T>` - Wrap LINQ expressions
- Implicit conversion to/from `Expression<Func<T, bool>>`

## Installation

```bash
dotnet add package Framework.Specifications
```

## Usage

### Define Specifications

```csharp
public sealed class IsActiveUserSpec : Specification<User>
{
    public override Expression<Func<User, bool>> ToExpression()
    {
        return user => user.IsActive && !user.IsDeleted;
    }
}

public sealed class HasPremiumPlanSpec : Specification<User>
{
    public override Expression<Func<User, bool>> ToExpression()
    {
        return user => user.PlanType == PlanType.Premium;
    }
}
```

### Combine Specifications

```csharp
var isActiveUser = new IsActiveUserSpec();
var hasPremiumPlan = new HasPremiumPlanSpec();

// Combine with AND
var activePremiumUser = isActiveUser.And(hasPremiumPlan);

// Combine with OR
var activeOrPremium = isActiveUser.Or(hasPremiumPlan);

// Negate
var inactiveUser = isActiveUser.Not();
```

### Use with EF Core

```csharp
var spec = new IsActiveUserSpec().And(new HasPremiumPlanSpec());
var users = await dbContext.Users.Where(spec).ToListAsync();
```

### Ad-hoc Specifications

```csharp
var spec = Specification<User>.FromExpression(u => u.Age > 18);
```

## Configuration

No configuration required.

## Dependencies

None.

## Side Effects

None.
