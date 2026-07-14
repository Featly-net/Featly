using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Featly.Server.Endpoints;

/// <summary>
/// Endpoint filter that enforces <see cref="WritePayloadLimits"/> on the
/// flag / config / segment write routes (issue #206). Centralizing the check in
/// one filter keeps every create + update handler free of a repeated guard: it
/// inspects the bound request argument and short-circuits with <c>400</c> when
/// the payload exceeds a cap, otherwise forwards to the handler.
/// </summary>
internal sealed class PayloadLimitFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            var error = argument switch
            {
                FlagWriteRequest flag => WritePayloadLimits.ValidateFlag(flag.Variants, flag.Rules),
                ConfigWriteRequest config => WritePayloadLimits.ValidateConfig(config.Rules),
                SegmentWriteRequest segment => WritePayloadLimits.ValidateConditions(segment.Conditions),
                _ => null,
            };
            if (error is not null)
            {
                return Results.BadRequest(new { error });
            }
        }

        return await next(context).ConfigureAwait(false);
    }
}

/// <summary>Fluent helper for attaching <see cref="PayloadLimitFilter"/> to a write route.</summary>
internal static class PayloadLimitFilterExtensions
{
    public static TBuilder RequirePayloadLimits<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(new PayloadLimitFilter());
}
