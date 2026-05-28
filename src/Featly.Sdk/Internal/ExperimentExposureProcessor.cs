using System.Collections.Concurrent;

namespace Featly.Sdk.Internal;

/// <summary>
/// Bridges flag evaluation to experiment telemetry. When an active experiment
/// covers an evaluated flag, this:
/// <list type="bullet">
///   <item>pins the subject's variant first-write-wins when the experiment opts
///   into <see cref="Experiment.StickyAssignments"/>, so a later weight change
///   does not migrate an already-exposed subject (process-local, no
///   server round-trip), and</item>
///   <item>emits one <see cref="EventType.Exposure"/> event per
///   (experiment, subject) for the process lifetime.</item>
/// </list>
/// Process-local state is intentional: it honours stickiness without a network
/// hop, consistent with Featly's local-first evaluation. The server still
/// persists the authoritative <c>Assignment</c> on first ingest.
/// </summary>
internal sealed class ExperimentExposureProcessor(IEventSink sink)
{
    // NUL separator cannot occur inside an experiment or subject key, so the
    // composed cache key is collision-free.
    private const char Separator = '\u0000';

    // (experimentKey + subjectKey) -> pinned variant, first write wins.
    private readonly ConcurrentDictionary<string, string> _sticky = new(StringComparer.Ordinal);

    // (experimentKey + subjectKey) already exposed this process.
    private readonly ConcurrentDictionary<string, byte> _exposed = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the exposure and returns the variant the caller should honour —
    /// the freshly evaluated one, or the pinned one for a sticky experiment.
    /// </summary>
    public string Process(Experiment experiment, string subjectKey, string evaluatedVariant)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var key = experiment.Key + Separator + subjectKey;

        var variant = experiment.StickyAssignments
            ? _sticky.GetOrAdd(key, evaluatedVariant)
            : evaluatedVariant;

        if (_exposed.TryAdd(key, 0))
        {
            sink.Enqueue(new QueuedEvent(
                Type: EventType.Exposure,
                SubjectKey: subjectKey,
                FlagKey: experiment.FlagKey,
                VariantKey: variant,
                At: DateTimeOffset.UtcNow));
        }

        return variant;
    }
}
