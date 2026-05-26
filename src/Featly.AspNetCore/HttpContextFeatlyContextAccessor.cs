using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Featly.AspNetCore;

/// <summary>
/// <see cref="IFeatlyContextAccessor"/> backed by ASP.NET Core's
/// <see cref="IHttpContextAccessor"/>. Maps the current request's
/// <see cref="ClaimsPrincipal"/> into an <see cref="EvaluationContext"/> so
/// SDK calls inside controllers / minimal API handlers / middlewares pick
/// the right targeting key and attributes automatically.
/// </summary>
/// <remarks>
/// The default mapping is intentionally tiny:
/// <list type="bullet">
///   <item><see cref="EvaluationContext.TargetingKey"/> ← the first non-empty of
///         <c>NameIdentifier</c>, <c>Sub</c>, <see cref="ClaimsIdentity.Name"/>.</item>
///   <item><see cref="EvaluationContext.Attributes"/> ← every claim, keyed by claim type,
///         plus a small set of convenience keys (<c>user.id</c>, <c>user.email</c>,
///         <c>user.name</c>).</item>
/// </list>
/// Consumers needing richer attribute mapping (request IP, locale, custom
/// headers) can replace this with their own <see cref="IFeatlyContextAccessor"/>
/// via <c>builder.UseContextAccessor&lt;MyAccessor&gt;()</c>.
/// </remarks>
public sealed class HttpContextFeatlyContextAccessor(IHttpContextAccessor httpContextAccessor) : IFeatlyContextAccessor
{
    /// <inheritdoc />
    public EvaluationContext? Current
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            var principal = context.User;
            if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
            {
                return null;
            }

            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var claim in principal.Claims)
            {
                attributes.TryAdd(claim.Type, claim.Value);
            }

            // Friendly shorthands so flag conditions don't need to spell out
            // the long URI-style claim types.
            var email = principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("email");
            if (!string.IsNullOrWhiteSpace(email))
            {
                attributes["user.email"] = email;
            }

            var name = principal.Identity.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                attributes["user.name"] = name;
            }

            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub")
                ?? name;
            if (!string.IsNullOrWhiteSpace(id))
            {
                attributes["user.id"] = id;
            }

            var targetingKey = id;

            return new EvaluationContext(
                TargetingKey: string.IsNullOrWhiteSpace(targetingKey) ? null : targetingKey,
                Attributes: attributes);
        }
    }
}
