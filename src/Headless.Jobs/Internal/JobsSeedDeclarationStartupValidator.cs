// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Internal;

/// <summary>
/// Rejects recurring-job declarations the seed could not write as one row each: two declarations with one key, which
/// would collide on one seeded id, and a declaration whose job this host does not register, or whose options are
/// invalid. The <c>seed</c> verb runs startup validators before any seeder, so a bad declaration fails before the seed
/// writes anything, and a normal start reports it instead of carrying it until the next deploy. A declaration whose
/// cron cannot fire is rejected too: the seed restores the declared expression over the stored one, so an unresolved
/// <c>%name%</c>, a typo, or an unknown zone would otherwise replace a working schedule with one that never runs.
/// </summary>
/// <param name="services">
/// Provider the check resolves from inside <c>ValidateAsync</c>: the declarations and the catalog are final only after
/// every registration has run.
/// </param>
internal sealed class JobsSeedDeclarationStartupValidator(IServiceProvider services) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        var declarations = services.GetRequiredService<JobsExecutionContext>().OptionsSeeding?.RecurringJobDeclarations;

        // Nothing declared, nothing to resolve: the common host pays no catalog build here.
        if (declarations is null || declarations.Count == 0)
        {
            return Task.CompletedTask;
        }

        var registry = services.GetRequiredService<JobFunctionRegistry>();
        var policies = services.GetRequiredService<JobSchedulingPolicies>();
        var scheduleCache = services.GetRequiredService<CronScheduleCache>();
        var configuration = services.GetService<IConfiguration>();
        var utcNow = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var declaration in declarations)
        {
            if (!keys.Add(declaration.Key))
            {
                if (duplicates.Add(declaration.Key))
                {
                    errors.Add(
                        $"Recurring job declaration key '{declaration.Key}' is declared more than once; give each declaration its own key."
                    );
                }

                continue;
            }

            var descriptor = declaration.TryResolve(registry, out var error);

            if (descriptor is null)
            {
                errors.Add(error!);
                continue;
            }

            try
            {
                _ = policies.ResolveRecurring(descriptor, declaration.Options);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Recurring job declaration '{declaration.Key}' has invalid options: {ex.Message}");
            }

            var cronError = _ValidateCron(declaration, scheduleCache, configuration, utcNow);

            if (cronError is not null)
            {
                errors.Add(cronError);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', errors));
        }

        return Task.CompletedTask;
    }

    // Mirrors the runtime ScheduleRecurringAsync check, after the same %name% resolution the seed applies.
    private static string? _ValidateCron(
        RecurringJobDeclaration declaration,
        CronScheduleCache scheduleCache,
        IConfiguration? configuration,
        DateTime utcNow
    )
    {
        var timeZoneId = declaration.Options?.TimeZoneId;

        if (!scheduleCache.CanResolveTimeZone(timeZoneId))
        {
            return $"Recurring job declaration '{declaration.Key}' names time zone '{timeZoneId}', which this host cannot resolve.";
        }

        var expression = JobFunctionRegistryBuilder.ResolveCronExpression(declaration.CronExpression, configuration);

        if (expression.StartsWith('%'))
        {
            return $"Recurring job declaration '{declaration.Key}' reads its cron from configuration key '{expression.Trim('%')}', which is not set.";
        }

        DateTime? next;

        try
        {
            next = scheduleCache.GetNextOccurrenceOrDefault(expression, utcNow, timeZoneId);
        }
        catch (ArgumentException)
        {
            next = null;
        }

        return next is null
            ? $"Recurring job declaration '{declaration.Key}' has cron expression '{expression}', which is invalid or has no future occurrence."
            : null;
    }
}
