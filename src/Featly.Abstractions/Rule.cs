namespace Featly;

/// <summary>
/// A targeting rule. The engine walks a flag's rules ordered by
/// <see cref="Order"/> ascending and the first rule whose <see cref="Conditions"/>
/// all match (AND) selects the outcome. <see cref="Enabled"/> lets operators
/// disable a rule without deleting it.
/// </summary>
public sealed class Rule
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

    /// <summary>What the engine serves when the rule matches.</summary>
    public required RuleOutcome Outcome { get; set; }

    /// <summary>Operators can disable a rule without deleting it.</summary>
    public bool Enabled { get; set; } = true;
}
