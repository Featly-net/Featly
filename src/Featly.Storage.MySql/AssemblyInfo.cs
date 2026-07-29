using System.Runtime.CompilerServices;

// This provider has no facade yet (PR 1 of issue #276 -- see FeatlyDbContext's
// remarks: IFeatlyStore can't be partially implemented), so its tests reach
// the concrete store classes and FeatlyDbContext directly, same as the
// Postgres and SQL Server providers did at the equivalent stage. Zero public
// API surface change; this stays internal-to-internal.
[assembly: InternalsVisibleTo("Featly.Storage.MySql.Tests")]
