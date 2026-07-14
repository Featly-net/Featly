namespace Featly.Authorization;

/// <summary>
/// The identifier + display name pair the server resolves from the
/// authenticated principal (cookie session or Bearer API key) and passes to the
/// permission pipeline. Kept deliberately tiny so the auth layer doesn't need to
/// round-trip the full <see cref="User"/> entity from storage on every request.
/// </summary>
/// <param name="Identifier">
/// Auth-stable identifier (email, OIDC <c>sub</c>, basic-auth username, …).
/// Must match <see cref="User.Identifier"/> for the lookup to resolve.
/// </param>
/// <param name="DisplayName">Human-readable name; falls back to <paramref name="Identifier"/> if no friendly name exists.</param>
public sealed record ResolvedUser(string Identifier, string DisplayName);
