using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;

namespace PkgDiff.API.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValidatePackageRouteAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var registry = ResolveRegistry(context);
        if (!PackageIdRules.IsKnownRegistry(registry))
        {
            context.Result = new BadRequestObjectResult(new { error = "Unknown registry." });
            return;
        }

        if (context.ActionArguments.TryGetValue("packageId", out var rawPackageId))
        {
            var packageId = DecodeOnce(rawPackageId as string);
            if (packageId is null || !PackageIdRules.IsValidPackageId(registry, packageId))
            {
                context.Result = new BadRequestObjectResult(new { error = "Invalid package id." });
                return;
            }

            context.ActionArguments["packageId"] = packageId;
        }

        if (context.ActionArguments.TryGetValue("q", out var query)
            && query is string q
            && q.Length > PackageIdRules.MaxQueryLength)
        {
            context.Result = new BadRequestObjectResult(new { error = "Query is too long." });
            return;
        }

        if (await IsRegistryDisabledAsync(context, registry!))
        {
            context.Result = new BadRequestObjectResult(new { error = "Registry is disabled." });
            return;
        }

        await next();
    }

    private static string? ResolveRegistry(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("registry", out var argument) && argument is string fromArgument)
            return fromArgument;

        if (context.RouteData.Values.TryGetValue("registry", out var routeValue) && routeValue is string fromRoute)
            return fromRoute;

        return context.ActionArguments.Values.OfType<AnalyzeRequest>().FirstOrDefault()?.Registry;
    }

    private static string? DecodeOnce(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static async Task<bool> IsRegistryDisabledAsync(ActionExecutingContext context, string registry)
    {
        var services = context.HttpContext.RequestServices;
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        // pub and pub.dev are aliases for the same registry row
        var keys = RegistryNames.SpellingsOf(registry);

        try
        {
            var rows = await unitOfWork.Registries.FindAsync(r => keys.Contains(r.Value.ToLower()));
            return rows.Any(r => r.Disabled);
        }
        catch (Exception ex)
        {
            // A registry-table outage must not take package analysis down with it
            var logger = services.GetService<ILogger<ValidatePackageRouteAttribute>>();
            logger?.LogWarning(ex, "Could not check whether registry {Registry} is disabled", registry);
            return false;
        }
    }
}
