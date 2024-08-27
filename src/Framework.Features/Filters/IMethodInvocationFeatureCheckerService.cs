using System.Reflection;
using Framework.Features.Checkers;

namespace Framework.Features.Filters;

public interface IMethodInvocationFeatureCheckerService
{
    Task CheckAsync(MethodInvocationFeatureCheckerContext context);
}

public sealed record MethodInvocationFeatureCheckerContext(MethodInfo Method);

public sealed class MethodInvocationFeatureCheckerService : IMethodInvocationFeatureCheckerService
{
    private readonly IFeatureChecker _featureChecker;

    public MethodInvocationFeatureCheckerService(IFeatureChecker featureChecker)
    {
        _featureChecker = featureChecker;
    }

    public async Task CheckAsync(MethodInvocationFeatureCheckerContext context)
    {
        if (_IsFeatureCheckDisabled(context))
        {
            return;
        }

        foreach (var requiresFeatureAttribute in _GetRequiredFeatureAttributes(context.Method))
        {
            await _featureChecker.CheckEnabledAsync(requiresFeatureAttribute.IsAnd, requiresFeatureAttribute.Features);
        }
    }

    private static bool _IsFeatureCheckDisabled(MethodInvocationFeatureCheckerContext context)
    {
        return context.Method.GetCustomAttributes(inherit: true).OfType<DisableFeatureCheckAttribute>().Any();
    }

    private static IEnumerable<RequiresFeatureAttribute> _GetRequiredFeatureAttributes(MethodInfo methodInfo)
    {
        var attributes = methodInfo.GetCustomAttributes(true).OfType<RequiresFeatureAttribute>();

        if (methodInfo.IsPublic)
        {
            attributes = attributes.Union(
                methodInfo.DeclaringType!.GetCustomAttributes(true).OfType<RequiresFeatureAttribute>()
            );
        }

        return attributes;
    }
}
