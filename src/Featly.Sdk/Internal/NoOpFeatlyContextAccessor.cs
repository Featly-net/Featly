namespace Featly.Sdk.Internal;

/// <summary>
/// Default <see cref="IFeatlyContextAccessor"/> wired by
/// <c>AddFeatly()</c>: always returns <c>null</c>, so the SDK falls back to
/// the explicit context (or no context) when callers don't override it.
/// Consumers that want ambient context replace this registration — for
/// example by calling <c>UseHttpContextAccessor()</c> from
/// <c>Featly.AspNetCore</c>.
/// </summary>
internal sealed class NoOpFeatlyContextAccessor : IFeatlyContextAccessor
{
    public EvaluationContext? Current => null;
}
