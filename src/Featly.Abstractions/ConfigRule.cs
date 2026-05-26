using System.Text.Json;

namespace Featly;

/// <summary>
/// A targeting rule for a <see cref="Config"/>. Shares <see cref="Condition"/>
/// with <see cref="Rule"/> but the outcome is a typed value served directly
/// (no variant indirection — configs have a single shape declared by
/// <see cref="Config.Type"/>).
/// </summary>
public sealed class ConfigRule
{
    /// <summary>Stable identifier for the rule. Survives reorders and edits.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Ordering key. Lower numbers evaluate first.</summary>
    public int Order { get; set; }

    /// <summary>Optional name shown in the dashboard.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Predicates the subject must satisfy. AND between conditions; the rule
    /// matches when every condition evaluates to true.
    /// </summary>
    public List<Condition> Conditions { get; set; } = [];

    /// <summary>
    /// The typed value served when the rule matches. Must be JSON-compatible
    /// with the owning <see cref="Config.Type"/>.
    /// </summary>
    public required JsonElement Value { get; set; }

    /// <summary>Operators can disable a rule without deleting it.</summary>
    public bool Enabled { get; set; } = true;
}
