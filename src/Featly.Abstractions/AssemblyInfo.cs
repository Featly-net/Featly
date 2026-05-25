using System.Runtime.CompilerServices;

// Internals visible to engine and tests so we can keep contract types
// without redundant duplication across layers.
[assembly: InternalsVisibleTo("Featly.Engine")]
[assembly: InternalsVisibleTo("Featly.Sdk")]
[assembly: InternalsVisibleTo("Featly.Server")]
[assembly: InternalsVisibleTo("Featly.Storage.Abstractions")]
[assembly: InternalsVisibleTo("Featly.Storage.InMemory")]
[assembly: InternalsVisibleTo("Featly.Storage.Sqlite")]
[assembly: InternalsVisibleTo("Featly.AspNetCore")]
[assembly: InternalsVisibleTo("Featly.OpenFeature.Provider")]
[assembly: InternalsVisibleTo("Featly.Cli")]
[assembly: InternalsVisibleTo("Featly.Engine.Tests")]
[assembly: InternalsVisibleTo("Featly.Sdk.Tests")]
[assembly: InternalsVisibleTo("Featly.Server.Tests")]
[assembly: InternalsVisibleTo("Featly.Storage.Sqlite.Tests")]
[assembly: InternalsVisibleTo("Featly.E2E.Tests")]
