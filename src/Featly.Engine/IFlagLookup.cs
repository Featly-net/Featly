namespace Featly.Engine;

/// <summary>
/// Resolves a <see cref="Flag"/> by key for the engine, so a flag's
/// <see cref="Prerequisite"/>s can be checked against the *other* flags in
/// the same snapshot (ADR-0027). Implementations typically wrap the same
/// in-memory snapshot as <see cref="ISegmentLookup"/> — there is no HTTP or
/// IO involved on the hot path.
/// </summary>
public interface IFlagLookup
{
    /// <summary>
    /// Returns the flag with the given key, or <c>false</c> + <c>null</c>
    /// when missing. The engine treats a missing prerequisite flag as an
    /// unmet prerequisite.
    /// </summary>
    bool TryGet(string key, out Flag? flag);
}

/// <summary>
/// An <see cref="IFlagLookup"/> backed by a pre-built dictionary. Suitable
/// for snapshot-based evaluation (SDK and server preview).
/// </summary>
public sealed class DictionaryFlagLookup(IReadOnlyDictionary<string, Flag>? flags) : IFlagLookup
{
    /// <summary>A shared, always-empty lookup. Useful when no flags are configured.</summary>
    public static readonly DictionaryFlagLookup Empty = new(null);

    /// <inheritdoc />
    public bool TryGet(string key, out Flag? flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (flags is null)
        {
            flag = null;
            return false;
        }

        if (flags.TryGetValue(key, out var found) && found is not null)
        {
            flag = found;
            return true;
        }

        flag = null;
        return false;
    }
}
