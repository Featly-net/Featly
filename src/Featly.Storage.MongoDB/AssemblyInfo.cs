using System.Runtime.CompilerServices;

// This provider has no facade yet (PR 1 of issue #277 — IFeatlyStore can't be
// partially implemented), so its tests reach the concrete store classes and
// class maps directly, same as every relational provider did at the
// equivalent stage. Zero public API surface change; this stays
// internal-to-internal.
[assembly: InternalsVisibleTo("Featly.Storage.MongoDB.Tests")]
