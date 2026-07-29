using System.Runtime.CompilerServices;

// Tests reach the concrete store classes and FeatlyDbContext directly (in
// addition to the public AddFeatlySqlServerStore() facade, exercised by
// SqlServerStoreRegistrationTests), same as the Postgres provider. Zero
// public API surface change; this stays internal-to-internal.
[assembly: InternalsVisibleTo("Featly.Storage.SqlServer.Tests")]
