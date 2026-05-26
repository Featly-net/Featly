using Featly.Sdk;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Featly.AspNetCore;

/// <summary>
/// DI extensions for the ASP.NET Core integration package.
/// </summary>
public static class FeatlyAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Wires <see cref="HttpContextFeatlyContextAccessor"/> so the SDK pulls
    /// the ambient <see cref="EvaluationContext"/> from
    /// <c>HttpContext.User</c> automatically. Also registers
    /// <see cref="IHttpContextAccessor"/> if the caller has not already.
    /// </summary>
    public static FeatlyClientBuilder UseHttpContextAccessor(this FeatlyClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        return builder.UseContextAccessor<HttpContextFeatlyContextAccessor>();
    }
}
