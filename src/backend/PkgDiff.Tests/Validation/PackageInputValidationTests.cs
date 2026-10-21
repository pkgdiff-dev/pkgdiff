using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PkgDiff.API.Filters;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Validation;

public class PackageInputValidationTests
{
    [Theory]
    [InlineData("nuget")]
    [InlineData("npm")]
    [InlineData("pypi")]
    [InlineData("pub")]
    [InlineData("pub.dev")]
    [InlineData("NuGet")]
    [InlineData("NPM")]
    [InlineData("PUB.DEV")]
    public void IsKnownRegistry_AllowlistedKey_ReturnsTrue(string registry)
    {
        Assert.True(PackageIdRules.IsKnownRegistry(registry));
    }

    [Theory]
    [InlineData("cargo")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("npm ")]
    public void IsKnownRegistry_UnknownKey_ReturnsFalse(string? registry)
    {
        Assert.False(PackageIdRules.IsKnownRegistry(registry));
    }

    [Theory]
    [InlineData("react")]
    [InlineData("@angular/core")]
    [InlineData("@types/node")]
    [InlineData("lodash.debounce")]
    [InlineData("JSONStream")]
    [InlineData("left-pad")]
    public void IsValidPackageId_ValidNpmName_ReturnsTrue(string packageId)
    {
        Assert.True(PackageIdRules.IsValidPackageId("npm", packageId));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("@scope/")]
    [InlineData("@Scope/x")]
    [InlineData("a b")]
    [InlineData("a?b")]
    [InlineData("react\n")]
    [InlineData("")]
    public void IsValidPackageId_InvalidNpmName_ReturnsFalse(string packageId)
    {
        Assert.False(PackageIdRules.IsValidPackageId("npm", packageId));
    }

    [Fact]
    public void IsValidPackageId_NameLongerThan214_ReturnsFalse()
    {
        Assert.True(PackageIdRules.IsValidPackageId("npm", new string('a', 214)));
        Assert.False(PackageIdRules.IsValidPackageId("npm", new string('a', 215)));
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Microsoft.Extensions.Http")]
    [InlineData("xunit.v3")]
    [InlineData("dotnet-ef")]
    [InlineData("My_Package")]
    public void IsValidPackageId_ValidNuGetId_ReturnsTrue(string packageId)
    {
        Assert.True(PackageIdRules.IsValidPackageId("nuget", packageId));
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData(".x")]
    [InlineData("a/b")]
    [InlineData("a b")]
    public void IsValidPackageId_InvalidNuGetId_ReturnsFalse(string packageId)
    {
        Assert.False(PackageIdRules.IsValidPackageId("nuget", packageId));
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("zope.interface")]
    [InlineData("typing_extensions")]
    [InlineData("Django")]
    [InlineData("a")]
    public void IsValidPackageId_ValidPyPIName_ReturnsTrue(string packageId)
    {
        Assert.True(PackageIdRules.IsValidPackageId("pypi", packageId));
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("x-")]
    [InlineData("a/b")]
    public void IsValidPackageId_InvalidPyPIName_ReturnsFalse(string packageId)
    {
        Assert.False(PackageIdRules.IsValidPackageId("pypi", packageId));
    }

    [Theory]
    [InlineData("pub", "http")]
    [InlineData("pub", "flutter_bloc")]
    [InlineData("pub", "firebase_core")]
    [InlineData("pub.dev", "http")]
    [InlineData("pub.dev", "flutter_bloc")]
    [InlineData("pub.dev", "firebase_core")]
    public void IsValidPackageId_ValidPubName_ReturnsTrue(string registry, string packageId)
    {
        Assert.True(PackageIdRules.IsValidPackageId(registry, packageId));
    }

    [Theory]
    [InlineData("pub", "Http")]
    [InlineData("pub", "a-b")]
    [InlineData("pub", "a/b")]
    [InlineData("pub.dev", "Http")]
    [InlineData("pub.dev", "a-b")]
    [InlineData("pub.dev", "a/b")]
    public void IsValidPackageId_InvalidPubName_ReturnsFalse(string registry, string packageId)
    {
        Assert.False(PackageIdRules.IsValidPackageId(registry, packageId));
    }

    [Fact]
    public void IsValidPackageId_UnknownRegistry_ReturnsFalse()
    {
        Assert.False(PackageIdRules.IsValidPackageId("cargo", "serde"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2.3")]
    [InlineData("1.0.0-beta.1+build.5")]
    [InlineData("1!2.0.post1")]
    public void IsValidVersion_ValidVersion_ReturnsTrue(string? version)
    {
        Assert.True(PackageIdRules.IsValidVersion(version));
    }

    [Theory]
    [InlineData("1.0 beta")]
    [InlineData("<script>")]
    [InlineData("1.0.0\n")]
    public void IsValidVersion_InvalidVersion_ReturnsFalse(string version)
    {
        Assert.False(PackageIdRules.IsValidVersion(version));
    }

    [Fact]
    public void IsValidVersion_LongerThan128_ReturnsFalse()
    {
        Assert.True(PackageIdRules.IsValidVersion(new string('1', 128)));
        Assert.False(PackageIdRules.IsValidVersion(new string('1', 129)));
    }

    [Fact]
    public void AnalyzeRequest_ScopedNpmNameWithEmptyVersions_IsValid()
    {
        var request = new AnalyzeRequest { Registry = "npm", PackageId = "@angular/core", CurrentVersion = "", TargetVersion = "" };

        Assert.Empty(ValidateModel(request));
    }

    [Fact]
    public void AnalyzeRequest_PathTraversalId_IsInvalid()
    {
        var request = new AnalyzeRequest { Registry = "npm", PackageId = "../x" };

        var results = ValidateModel(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AnalyzeRequest.PackageId)));
    }

    [Fact]
    public void AnalyzeRequest_UnknownRegistry_IsInvalid()
    {
        var request = new AnalyzeRequest { Registry = "cargo", PackageId = "x" };

        var results = ValidateModel(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AnalyzeRequest.Registry)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnalyzeRequest_EmptyPackageId_IsInvalid(string? packageId)
    {
        var request = new AnalyzeRequest { Registry = "nuget", PackageId = packageId! };

        var results = ValidateModel(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AnalyzeRequest.PackageId)));
    }

    [Fact]
    public void AnalyzeRequest_InvalidVersion_IsInvalid()
    {
        var request = new AnalyzeRequest { Registry = "nuget", PackageId = "Newtonsoft.Json", TargetVersion = "<script>" };

        var results = ValidateModel(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AnalyzeRequest.TargetVersion)));
    }

    [Fact]
    public void AnalyzeRequest_OverlongProgressToken_IsInvalid()
    {
        var request = new AnalyzeRequest { Registry = "nuget", PackageId = "Newtonsoft.Json", ProgressToken = new string('a', 1025) };

        var results = ValidateModel(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AnalyzeRequest.ProgressToken)));
    }

    [Fact]
    public async Task Filter_DisabledRegistryRow_ReturnsBadRequest()
    {
        var unitOfWork = new FakeUnitOfWork();
        await unitOfWork.Registries.AddAsync(new Registry { Value = "npm", Disabled = true });
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["packageId"] = "react" };

        var (context, nextCalled) = await RunFilterAsync(unitOfWork, arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Filter_DisabledPubRow_AlsoRejectsPubDevAlias()
    {
        var unitOfWork = new FakeUnitOfWork();
        await unitOfWork.Registries.AddAsync(new Registry { Value = "pub", Disabled = true });
        var arguments = new Dictionary<string, object?> { ["registry"] = "pub.dev", ["packageId"] = "http" };

        var (context, _) = await RunFilterAsync(unitOfWork, arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Fact]
    public async Task Filter_KnownRegistryWithoutRow_InvokesNext()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "nuget", ["packageId"] = "Newtonsoft.Json" };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.Null(context.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Filter_EnabledRegistryRow_InvokesNext()
    {
        var unitOfWork = new FakeUnitOfWork();
        await unitOfWork.Registries.AddAsync(new Registry { Value = "NPM", Disabled = false });
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["packageId"] = "react" };

        var (_, nextCalled) = await RunFilterAsync(unitOfWork, arguments);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Filter_PercentEncodedScopedId_IsDecodedOnceBeforeReachingAction()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["packageId"] = "%40angular%2Fcore" };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.True(nextCalled());
        Assert.Equal("@angular/core", context.ActionArguments["packageId"]);
    }

    [Fact]
    public async Task Filter_DoubleEncodedId_IsNotDecodedTwice()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["packageId"] = "%252e%252e%252fx" };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Filter_UnknownRegistry_ReturnsBadRequest()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "cargo", ["packageId"] = "serde" };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Filter_InvalidRoutePackageId_ReturnsBadRequest()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "nuget", ["packageId"] = "..%2F..%2Fadmin" };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Filter_QueryLongerThanCap_ReturnsBadRequest()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["q"] = new string('a', 215), ["limit"] = 5 };

        var (context, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Filter_QueryWithinCap_InvokesNext()
    {
        var arguments = new Dictionary<string, object?> { ["registry"] = "npm", ["q"] = "reac", ["limit"] = 5 };

        var (_, nextCalled) = await RunFilterAsync(new FakeUnitOfWork(), arguments);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Filter_AnalyzeRequestBody_UsesItsRegistryForTheDisabledCheck()
    {
        var unitOfWork = new FakeUnitOfWork();
        await unitOfWork.Registries.AddAsync(new Registry { Value = "pypi", Disabled = true });
        var arguments = new Dictionary<string, object?>
        {
            ["request"] = new AnalyzeRequest { Registry = "pypi", PackageId = "requests" }
        };

        var (context, _) = await RunFilterAsync(unitOfWork, arguments);

        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Fact]
    public async Task FakeUnitOfWork_AddAsync_AssignsIncrementingIds()
    {
        var unitOfWork = new FakeUnitOfWork();

        var first = await unitOfWork.Registries.AddAsync(new Registry { Value = "npm" });
        var second = await unitOfWork.Registries.AddAsync(new Registry { Value = "nuget" });

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal(2, unitOfWork.Registries.Items.Count);
    }

    [Fact]
    public async Task FakeUnitOfWork_FindAsync_FiltersWithThePredicate()
    {
        IUnitOfWork unitOfWork = new FakeUnitOfWork();
        await unitOfWork.Registries.AddAsync(new Registry { Value = "npm", Disabled = true });
        await unitOfWork.Registries.AddAsync(new Registry { Value = "nuget" });

        var disabled = await unitOfWork.Registries.FindAsync(r => r.Disabled);

        Assert.Equal("npm", Assert.Single(disabled).Value);
    }

    [Fact]
    public async Task FakeUnitOfWork_SaveChangesAsync_CountsCalls()
    {
        var unitOfWork = new FakeUnitOfWork();

        await unitOfWork.SaveChangesAsync();
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(2, unitOfWork.SaveChangesCalls);
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static async Task<(ActionExecutingContext Context, Func<bool> NextCalled)> RunFilterAsync(
        FakeUnitOfWork unitOfWork,
        IDictionary<string, object?> arguments)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var context = new ActionExecutingContext(actionContext, filters, arguments, controller: new object());

        var called = false;
        await new ValidatePackageRouteAttribute().OnActionExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, filters, controller: new object()));
        });

        return (context, () => called);
    }
}
