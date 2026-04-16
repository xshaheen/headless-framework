using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Managers;

/// <summary>
/// Fluent chain job builder with lambda configuration and duplicate prevention
/// </summary>
public class FluentChainJobBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _rootTicker;
    private readonly bool[] _childrenUsed = new bool[5]; // Track which children are used
    private readonly bool[][] _grandChildrenUsed = new bool[5][]; // Track which grandchildren are used per child

    private FluentChainJobBuilder()
    {
        _rootTicker = new TTimeJob
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Children = new List<TTimeJob>(),
        };

        // Initialize grandchildren tracking
        for (int i = 0; i < 5; i++)
        {
            _grandChildrenUsed[i] = new bool[5];
        }
    }

    /// <summary>
    /// Start building by configuring the parent job
    /// </summary>
    public static FluentChainJobBuilder<TTimeJob> BeginWith(Action<ParentBuilder<TTimeJob>> configure)
    {
        var builder = new FluentChainJobBuilder<TTimeJob>();
        var parentBuilder = new ParentBuilder<TTimeJob>(builder._rootTicker);
        configure(parentBuilder);
        return builder;
    }

    /// <summary>
    /// Configure the first child (1/5)
    /// </summary>
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
    /// Configure the second child (2/5)
    /// </summary>
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
    /// Configure the third child (3/5)
    /// </summary>
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
    /// Configure the fourth child (4/5)
    /// </summary>
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
    /// Configure the fifth child (5/5)
    /// </summary>
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
        return new TTimeJob
        {
            Id = Guid.NewGuid(),
            ParentId = _rootTicker.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Children = new List<TTimeJob>(),
        };
    }

    private static TTimeJob _CreateGrandChild(TTimeJob parent)
    {
        return new TTimeJob
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Children = [],
        };
    }

    /// <summary>
    /// Build the final job entity
    /// </summary>
    public TTimeJob Build() => _rootTicker;

    /// <summary>
    /// Implicit conversion to entity
    /// </summary>
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
/// Parent builder for configuring the root job
/// </summary>
public class ParentBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _parent;

    internal ParentBuilder(TTimeJob parent)
    {
        _parent = parent;
    }

    public ParentBuilder<TTimeJob> SetFunction(string functionName)
    {
        _parent.Function = functionName;
        return this;
    }

    public ParentBuilder<TTimeJob> SetDescription(string description)
    {
        _parent.Description = description;
        return this;
    }

    public ParentBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _parent.ExecutionTime = executionTime;
        return this;
    }

    public ParentBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _parent.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    public ParentBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _parent.Retries = retries;
        _parent.RetryIntervals = intervals;
        return this;
    }
}

/// <summary>
/// Child builder for configuring individual children
/// </summary>
public class ChildBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _child;

    internal ChildBuilder(TTimeJob child)
    {
        _child = child;
    }

    public ChildBuilder<TTimeJob> SetFunction(string functionName)
    {
        _child.Function = functionName;
        return this;
    }

    public ChildBuilder<TTimeJob> SetDescription(string description)
    {
        _child.Description = description;
        return this;
    }

    public ChildBuilder<TTimeJob> SetRunCondition(RunCondition condition)
    {
        _child.RunCondition = condition;
        return this;
    }

    public ChildBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _child.ExecutionTime = executionTime;
        return this;
    }

    public ChildBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _child.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    public ChildBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _child.Retries = retries;
        _child.RetryIntervals = intervals;
        return this;
    }
}

/// <summary>
/// Grandchild builder for configuring individual grandchildren
/// </summary>
public class GrandChildBuilder<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    private readonly TTimeJob _grandChild;

    internal GrandChildBuilder(TTimeJob grandChild)
    {
        _grandChild = grandChild;
    }

    public GrandChildBuilder<TTimeJob> SetFunction(string functionName)
    {
        _grandChild.Function = functionName;
        return this;
    }

    public GrandChildBuilder<TTimeJob> SetDescription(string description)
    {
        _grandChild.Description = description;
        return this;
    }

    public GrandChildBuilder<TTimeJob> SetRunCondition(RunCondition condition)
    {
        _grandChild.RunCondition = condition;
        return this;
    }

    public GrandChildBuilder<TTimeJob> SetExecutionTime(DateTime executionTime)
    {
        _grandChild.ExecutionTime = executionTime;
        return this;
    }

    public GrandChildBuilder<TTimeJob> SetRequest<T>(T request)
    {
        _grandChild.Request = JobsHelper.CreateJobRequest(request);
        return this;
    }

    public GrandChildBuilder<TTimeJob> SetRetries(int retries, params int[] intervals)
    {
        _grandChild.Retries = retries;
        _grandChild.RetryIntervals = intervals;
        return this;
    }
}

/// <summary>
/// Extension methods for easier creation
/// </summary>
public static class FluentChainJobBuilderExtensions
{
    /// <summary>
    /// Start building a fluent chain job by configuring the parent
    /// </summary>
    public static FluentChainJobBuilder<TTimeJob> BeginWith<TTimeJob>(Action<ParentBuilder<TTimeJob>> configure)
        where TTimeJob : TimeJobEntity<TTimeJob>, new() => FluentChainJobBuilder<TTimeJob>.BeginWith(configure);
}
