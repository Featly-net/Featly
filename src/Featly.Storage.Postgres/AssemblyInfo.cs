using System.Runtime.CompilerServices;

// The Sqlite provider's tests exercise the full public IFeatlyStore facade
// (AddFeatlySqliteStore + the sub-store properties), so they never need to see
// internals. This provider has no facade yet (PR 1 of #157 — see
// FeatlyDbContext's remarks: IFeatlyStore can't be partially implemented), so
// its tests reach the concrete store classes and FeatlyDbContext directly.
// Zero public API surface change; this stays internal-to-internal.
[assembly: InternalsVisibleTo("Featly.Storage.Postgres.Tests")]
