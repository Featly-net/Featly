namespace Featly;

/// <summary>
/// Resolves an ambient <see cref="EvaluationContext"/> for the current call.
/// Registered in DI so the SDK can pull a context for every evaluation
/// without callers having to build one by hand.
/// </summary>
/// <remarks>
/// The default ASP.NET Core implementation lives in <c>Featly.AspNetCore</c>
/// and reads from <c>HttpContext.User</c> claims; consumers can replace it
/// with anything that fits their identity story. M3D wires this end-to-end.
/// </remarks>
public interface IFeatlyContextAccessor
{
    /// <summary>
    /// Returns the ambient context, or <c>null</c> when none is available
    /// (background workers, console apps without explicit context, etc.).
    /// Callers fall back to <see cref="EvaluationContext"/> defaults when this
    /// returns null.
    /// </summary>
    EvaluationContext? Current { get; }
}
