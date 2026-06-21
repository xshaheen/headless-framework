// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Managers;

/// <summary>
/// Fluent builder for constructing a tree of chained time jobs: one root parent with up to five child
/// jobs, each of which may have up to five grandchild jobs. Each level is configured via a typed
/// builder callback to prevent duplicate slot registration at compile time.
/// </summary>
/// <remarks>
/// Child and grandchild jobs run when the parent reaches a terminal status determined by their
/// <see cref="RunCondition"/>. Use <see cref="BeginWith"/> as the entry point, then chain
/// <c>WithFirstChild</c> … <c>WithFifthChild</c> on the returned builder or on a
/// <c>*ChildBuilder</c> instance. Call <see cref="Build"/> (or use the implicit conversion to
/// <typeparamref name="TTimeJob"/>) to obtain the root entity, then pass it to
/// <c>ITimeJobManager.AddAsync</c>.
/// </remarks>
/// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
public class FluentChainJobBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _rootTicker;
    private readonly bool[] _childrenUsed = new bool[5]; // Track which children are used
    private readonly bool[][] _grandChildrenUsed = new bool[5][]; // Track which grandchildren are used per child

    private FluentChainJobBuilder()
    {
        var now = DateTime.UtcNow;

        _rootTicker = new TTimeJob
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            Children = [],
        };

        // Initialize grandchildren tracking
        for (var i = 0; i < 5; i++)
        {
            _grandChildrenUsed[i] = new bool[5];
        }
    }

    /// <summary>
    /// Creates a new builder and immediately configures the root (parent) job via
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ParentBuilder{TTimeJob}"/> for the root job.</param>
    /// <returns>A builder ready to accept child configuration.</returns>
    public static FluentChainJobBuilder<TTimeJob> BeginWith(Action<ParentBuilder<TTimeJob>> configure)
    {
        var builder = new FluentChainJobBuilder<TTimeJob>();
        var parentBuilder = new ParentBuilder<TTimeJob>(builder._rootTicker);
        configure(parentBuilder);
        return builder;
    }

    /// <summary>
    /// Configures the first child job (slot 1 of 5). Throws if this slot was already configured.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ChildBuilder{TTimeJob}"/> for this child.</param>
    /// <exception cref="InvalidOperationException">This child slot has already been configured.</exception>
    public FirstChildBuilder WithFirstChild(Action<ChildBuilder<TTimeJob>> configure)
    {
        if (_childrenUsed[0])
        {
            throw new InvalidOperationException("First child has already been configured");
        }

        _childrenUsed[0] = true;
        var child = _CreateChild();
        var childBuilder = new ChildBuilder<TTimeJob>(child);
        configure(childBuilder);
        _rootTicker.Children.Add(child);

        return new FirstChildBuilder(this, child, 0);
    }

    /// <summary>
    /// Configures the second child job (slot 2 of 5). Throws if this slot was already configured.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ChildBuilder{TTimeJob}"/> for this child.</param>
    /// <exception cref="InvalidOperationException">This child slot has already been configured.</exception>
    public SecondChildBuilder WithSecondChild(Action<ChildBuilder<TTimeJob>> configure)
    {
        if (_childrenUsed[1])
        {
            throw new InvalidOperationException("Second child has already been configured");
        }

        _childrenUsed[1] = true;
        var child = _CreateChild();
        var childBuilder = new ChildBuilder<TTimeJob>(child);
        configure(childBuilder);
        _rootTicker.Children.Add(child);

        return new SecondChildBuilder(this, child, 1);
    }

    /// <summary>
    /// Configures the third child job (slot 3 of 5). Throws if this slot was already configured.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ChildBuilder{TTimeJob}"/> for this child.</param>
    /// <exception cref="InvalidOperationException">This child slot has already been configured.</exception>
    public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeJob>> configure)
    {
        if (_childrenUsed[2])
        {
            throw new InvalidOperationException("Third child has already been configured");
        }

        _childrenUsed[2] = true;
        var child = _CreateChild();
        var childBuilder = new ChildBuilder<TTimeJob>(child);
        configure(childBuilder);
        _rootTicker.Children.Add(child);

        return new ThirdChildBuilder(this, child, 2);
    }

    /// <summary>
    /// Configures the fourth child job (slot 4 of 5). Throws if this slot was already configured.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ChildBuilder{TTimeJob}"/> for this child.</param>
    /// <exception cref="InvalidOperationException">This child slot has already been configured.</exception>
    public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeJob>> configure)
    {
        if (_childrenUsed[3])
        {
            throw new InvalidOperationException("Fourth child has already been configured");
        }

        _childrenUsed[3] = true;
        var child = _CreateChild();
        var childBuilder = new ChildBuilder<TTimeJob>(child);
        configure(childBuilder);
        _rootTicker.Children.Add(child);

        return new FourthChildBuilder(this, child, 3);
    }

    /// <summary>
    /// Configures the fifth child job (slot 5 of 5). Throws if this slot was already configured.
    /// </summary>
    /// <param name="configure">Callback that receives a <see cref="ChildBuilder{TTimeJob}"/> for this child.</param>
    /// <exception cref="InvalidOperationException">This child slot has already been configured.</exception>
    public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeJob>> configure)
    {
        if (_childrenUsed[4])
        {
            throw new InvalidOperationException("Fifth child has already been configured");
        }

        _childrenUsed[4] = true;
        var child = _CreateChild();
        var childBuilder = new ChildBuilder<TTimeJob>(child);
        configure(childBuilder);
        _rootTicker.Children.Add(child);

        return new FifthChildBuilder(this, child, 4);
    }

    private TTimeJob _CreateChild()
    {
        var now = DateTime.UtcNow;

        return new TTimeJob
        {
            Id = Guid.NewGuid(),
            ParentId = _rootTicker.Id,
            CreatedAt = now,
            UpdatedAt = now,
            Children = [],
        };
    }

    private static TTimeJob _CreateGrandChild(TTimeJob parent)
    {
        var now = DateTime.UtcNow;

        return new TTimeJob
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            CreatedAt = now,
            UpdatedAt = now,
            Children = [],
        };
    }

    /// <summary>Returns the fully configured root <typeparamref name="TTimeJob"/> entity with all child links set.</summary>
    public TTimeJob Build() => _rootTicker;

    /// <summary>Implicit conversion to <typeparamref name="TTimeJob"/>; equivalent to calling <see cref="Build"/>.</summary>
    public static implicit operator TTimeJob(FluentChainJobBuilder<TTimeJob> builder) => builder.Build();

    // Individual child builders to prevent duplicate configuration
    public class FirstChildBuilder
    {
        private readonly FluentChainJobBuilder<TTimeJob> _mainBuilder;
        private readonly TTimeJob _child;
        private readonly int _childIndex;

        internal FirstChildBuilder(FluentChainJobBuilder<TTimeJob> mainBuilder, TTimeJob child, int childIndex)
        {
            _mainBuilder = mainBuilder;
            _child = child;
            _childIndex = childIndex;
        }

        public FirstChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][0])
            {
                throw new InvalidOperationException("First grandchild of first child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FirstChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][1])
            {
                throw new InvalidOperationException("Second grandchild of first child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FirstChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][2])
            {
                throw new InvalidOperationException("Third grandchild of first child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FirstChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][3])
            {
                throw new InvalidOperationException("Fourth grandchild of first child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FirstChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][4])
            {
                throw new InvalidOperationException("Fifth grandchild of first child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public SecondChildBuilder WithSecondChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithSecondChild(configure);

        public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithThirdChild(configure);

        public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFourthChild(configure);

        public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFifthChild(configure);

        public TTimeJob Build() => _mainBuilder.Build();

        public static implicit operator TTimeJob(FirstChildBuilder builder) => builder.Build();

        public TTimeJob ToTTimeJob() => Build();
    }

    public class SecondChildBuilder
    {
        private readonly FluentChainJobBuilder<TTimeJob> _mainBuilder;
        private readonly TTimeJob _child;
        private readonly int _childIndex;

        internal SecondChildBuilder(FluentChainJobBuilder<TTimeJob> mainBuilder, TTimeJob child, int childIndex)
        {
            _mainBuilder = mainBuilder;
            _child = child;
            _childIndex = childIndex;
        }

        public SecondChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][0])
            {
                throw new InvalidOperationException("First grandchild of second child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public SecondChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][1])
            {
                throw new InvalidOperationException("Second grandchild of second child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public SecondChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][2])
            {
                throw new InvalidOperationException("Third grandchild of second child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public SecondChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][3])
            {
                throw new InvalidOperationException("Fourth grandchild of second child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public SecondChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][4])
            {
                throw new InvalidOperationException("Fifth grandchild of second child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithThirdChild(configure);

        public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFourthChild(configure);

        public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFifthChild(configure);

        public TTimeJob Build() => _mainBuilder.Build();

        public static implicit operator TTimeJob(SecondChildBuilder builder) => builder.Build();

        public TTimeJob ToTTimeJob() => Build();
    }

    public class ThirdChildBuilder
    {
        private readonly FluentChainJobBuilder<TTimeJob> _mainBuilder;
        private readonly TTimeJob _child;
        private readonly int _childIndex;

        internal ThirdChildBuilder(FluentChainJobBuilder<TTimeJob> mainBuilder, TTimeJob child, int childIndex)
        {
            _mainBuilder = mainBuilder;
            _child = child;
            _childIndex = childIndex;
        }

        public ThirdChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][0])
            {
                throw new InvalidOperationException("First grandchild of third child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public ThirdChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][1])
            {
                throw new InvalidOperationException("Second grandchild of third child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public ThirdChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][2])
            {
                throw new InvalidOperationException("Third grandchild of third child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public ThirdChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][3])
            {
                throw new InvalidOperationException("Fourth grandchild of third child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public ThirdChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][4])
            {
                throw new InvalidOperationException("Fifth grandchild of third child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFourthChild(configure);

        public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFifthChild(configure);

        public TTimeJob Build() => _mainBuilder.Build();

        public static implicit operator TTimeJob(ThirdChildBuilder builder) => builder.Build();

        public TTimeJob ToTTimeJob() => Build();
    }

    public class FourthChildBuilder
    {
        private readonly FluentChainJobBuilder<TTimeJob> _mainBuilder;
        private readonly TTimeJob _child;
        private readonly int _childIndex;

        internal FourthChildBuilder(FluentChainJobBuilder<TTimeJob> mainBuilder, TTimeJob child, int childIndex)
        {
            _mainBuilder = mainBuilder;
            _child = child;
            _childIndex = childIndex;
        }

        public FourthChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][0])
            {
                throw new InvalidOperationException("First grandchild of fourth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FourthChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][1])
            {
                throw new InvalidOperationException("Second grandchild of fourth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FourthChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][2])
            {
                throw new InvalidOperationException("Third grandchild of fourth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FourthChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][3])
            {
                throw new InvalidOperationException("Fourth grandchild of fourth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FourthChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][4])
            {
                throw new InvalidOperationException("Fifth grandchild of fourth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeJob>> configure) =>
            _mainBuilder.WithFifthChild(configure);

        public TTimeJob Build() => _mainBuilder.Build();

        public static implicit operator TTimeJob(FourthChildBuilder builder) => builder.Build();

        public TTimeJob ToTTimeJob() => Build();
    }

    public class FifthChildBuilder
    {
        private readonly FluentChainJobBuilder<TTimeJob> _mainBuilder;
        private readonly TTimeJob _child;
        private readonly int _childIndex;

        internal FifthChildBuilder(FluentChainJobBuilder<TTimeJob> mainBuilder, TTimeJob child, int childIndex)
        {
            _mainBuilder = mainBuilder;
            _child = child;
            _childIndex = childIndex;
        }

        public FifthChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][0])
            {
                throw new InvalidOperationException("First grandchild of fifth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FifthChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][1])
            {
                throw new InvalidOperationException("Second grandchild of fifth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FifthChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][2])
            {
                throw new InvalidOperationException("Third grandchild of fifth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FifthChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][3])
            {
                throw new InvalidOperationException("Fourth grandchild of fifth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public FifthChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeJob>> configure)
        {
            if (_mainBuilder._grandChildrenUsed[_childIndex][4])
            {
                throw new InvalidOperationException("Fifth grandchild of fifth child has already been configured");
            }

            _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
            var grandChild = _CreateGrandChild(_child);
            var grandChildBuilder = new GrandChildBuilder<TTimeJob>(grandChild);
            configure(grandChildBuilder);
            _child.Children.Add(grandChild);
            return this;
        }

        public TTimeJob Build() => _mainBuilder.Build();

        public static implicit operator TTimeJob(FifthChildBuilder builder) => builder.Build();

        public TTimeJob ToTTimeJob() => Build();
    }

    public TTimeJob ToTTimeJob() => Build();
}

/// <summary>
/// Configures the root (parent) job in a <see cref="FluentChainJobBuilder{TTimeJob}"/> tree.
/// </summary>
/// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
public class ParentBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _parent;

    internal ParentBuilder(TTimeJob parent)
    {
        _parent = parent;
    }

    /// <summary>Sets the registered function name for the root job.</summary>
    /// <param name="functionName">Must match a <c>[JobFunction]</c>-annotated method name.</param>
    public ParentBuilder<TTimeJob> SetFunction(string functionName)
    {
        _parent.Function = functionName;
        return this;
    }

    /// <summary>Sets an optional human-readable description for the root job.</summary>
    /// <param name="description">Description stored on the job row.</param>
    public ParentBuilder<TTimeJob> SetDescription(string description)
    {
        _parent.Description = description;
        return this;
    }

    /// <summary>Sets the UTC date/time at which the root job should execute.</summary>
    /// <param name="executionTime">Desired execution time in UTC.</param>
    public ParentBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _parent.ExecutionTime = executionTime;
        return this;
    }

    /// <summary>Serializes <paramref name="request"/> and stores it as the job's request payload.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="request">The payload to serialize.</param>
    public ParentBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _parent.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    /// <summary>Configures retry behavior for the root job.</summary>
    /// <param name="retries">Maximum number of retry attempts.</param>
    /// <param name="intervals">Per-attempt retry delay in seconds.</param>
    public ParentBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _parent.Retries = retries;
        _parent.RetryIntervals = intervals;
        return this;
    }

    /// <summary>Sets the node-death policy applied when the executing node dies mid-execution.</summary>
    /// <param name="policy">The policy to apply.</param>
    public ParentBuilder<TTimeJob> SetOnNodeDeath(NodeDeathPolicy policy)
    {
        _parent.OnNodeDeath = policy;
        return this;
    }
}

/// <summary>
/// Configures an individual child job in a <see cref="FluentChainJobBuilder{TTimeJob}"/> tree.
/// Received as the argument to the <c>WithFirstChild</c> … <c>WithFifthChild</c> callbacks.
/// </summary>
/// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
public class ChildBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _child;

    internal ChildBuilder(TTimeJob child)
    {
        _child = child;
    }

    /// <summary>Sets the registered function name for this child job.</summary>
    /// <param name="functionName">Must match a <c>[JobFunction]</c>-annotated method name.</param>
    public ChildBuilder<TTimeJob> SetFunction(string functionName)
    {
        _child.Function = functionName;
        return this;
    }

    /// <summary>Sets an optional human-readable description for this child job.</summary>
    /// <param name="description">Description stored on the job row.</param>
    public ChildBuilder<TTimeJob> SetDescription(string description)
    {
        _child.Description = description;
        return this;
    }

    /// <summary>
    /// Sets the condition relative to the parent's terminal status that triggers this child job.
    /// </summary>
    /// <param name="condition">The run condition.</param>
    public ChildBuilder<TTimeJob> SetRunCondition(RunCondition condition)
    {
        _child.RunCondition = condition;
        return this;
    }

    /// <summary>Sets the UTC date/time at which this child job should execute.</summary>
    /// <param name="executionTime">Desired execution time in UTC.</param>
    public ChildBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _child.ExecutionTime = executionTime;
        return this;
    }

    /// <summary>Serializes <paramref name="request"/> and stores it as this child job's request payload.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="request">The payload to serialize.</param>
    public ChildBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _child.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    /// <summary>Configures retry behavior for this child job.</summary>
    /// <param name="retries">Maximum number of retry attempts.</param>
    /// <param name="intervals">Per-attempt retry delay in seconds.</param>
    public ChildBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _child.Retries = retries;
        _child.RetryIntervals = intervals;
        return this;
    }

    /// <summary>Sets the node-death policy applied when the executing node dies mid-execution.</summary>
    /// <param name="policy">The policy to apply.</param>
    public ChildBuilder<TTimeJob> SetOnNodeDeath(NodeDeathPolicy policy)
    {
        _child.OnNodeDeath = policy;
        return this;
    }
}

/// <summary>
/// Configures an individual grandchild job in a <see cref="FluentChainJobBuilder{TTimeJob}"/> tree.
/// Received as the argument to the <c>With*GrandChild</c> callbacks on a <c>*ChildBuilder</c>.
/// </summary>
/// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
public class GrandChildBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _grandChild;

    internal GrandChildBuilder(TTimeJob grandChild)
    {
        _grandChild = grandChild;
    }

    /// <summary>Sets the registered function name for this grandchild job.</summary>
    /// <param name="functionName">Must match a <c>[JobFunction]</c>-annotated method name.</param>
    public GrandChildBuilder<TTimeJob> SetFunction(string functionName)
    {
        _grandChild.Function = functionName;
        return this;
    }

    /// <summary>Sets an optional human-readable description for this grandchild job.</summary>
    /// <param name="description">Description stored on the job row.</param>
    public GrandChildBuilder<TTimeJob> SetDescription(string description)
    {
        _grandChild.Description = description;
        return this;
    }

    /// <summary>
    /// Sets the condition relative to the parent child's terminal status that triggers this grandchild.
    /// </summary>
    /// <param name="condition">The run condition.</param>
    public GrandChildBuilder<TTimeJob> SetRunCondition(RunCondition condition)
    {
        _grandChild.RunCondition = condition;
        return this;
    }

    /// <summary>Sets the UTC date/time at which this grandchild job should execute.</summary>
    /// <param name="executionTime">Desired execution time in UTC.</param>
    public GrandChildBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _grandChild.ExecutionTime = executionTime;
        return this;
    }

    /// <summary>Serializes <paramref name="request"/> and stores it as this grandchild's request payload.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="request">The payload to serialize.</param>
    public GrandChildBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _grandChild.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    /// <summary>Configures retry behavior for this grandchild job.</summary>
    /// <param name="retries">Maximum number of retry attempts.</param>
    /// <param name="intervals">Per-attempt retry delay in seconds.</param>
    public GrandChildBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _grandChild.Retries = retries;
        _grandChild.RetryIntervals = intervals;
        return this;
    }

    /// <summary>Sets the node-death policy applied when the executing node dies mid-execution.</summary>
    /// <param name="policy">The policy to apply.</param>
    public GrandChildBuilder<TTimeJob> SetOnNodeDeath(NodeDeathPolicy policy)
    {
        _grandChild.OnNodeDeath = policy;
        return this;
    }
}

/// <summary>
/// Extension methods providing a generic entry point into <see cref="FluentChainJobBuilder{TTimeJob}"/>
/// when the type argument can be inferred from the callback.
/// </summary>
public static class FluentChainJobBuilderExtensions
{
    /// <summary>
    /// Creates a <see cref="FluentChainJobBuilder{TTimeJob}"/> and configures its root job via
    /// <paramref name="configure"/>. Equivalent to <c>FluentChainJobBuilder&lt;TTimeJob&gt;.BeginWith(configure)</c>.
    /// </summary>
    /// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
    /// <param name="configure">Callback that receives a <see cref="ParentBuilder{TTimeJob}"/> for the root job.</param>
    /// <returns>A builder ready to accept child configuration.</returns>
    public static FluentChainJobBuilder<TTimeJob> BeginWith<TTimeJob>(Action<ParentBuilder<TTimeJob>> configure)
        where TTimeJob : TimeJobEntity<TTimeJob>, new() => FluentChainJobBuilder<TTimeJob>.BeginWith(configure);
}
