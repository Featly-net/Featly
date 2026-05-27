namespace Featly.Storage;

/// <summary>
/// Aggregate facade exposing the storage sub-stores. Application code (server,
/// CLI) depends only on this interface; concrete implementations live in
/// provider packages (<c>Featly.Storage.InMemory</c>, <c>Featly.Storage.Sqlite</c>).
/// </summary>
/// <remarks>
/// This type intentionally shares its name with the marker
/// <see cref="Featly.IFeatlyStore"/> in <c>Featly.Abstractions</c>; the marker
/// lets non-storage assemblies reference <see cref="IFeatlyStore"/> without
/// pulling in a storage dependency. The full facade is the one defined here.
/// </remarks>
public interface IFeatlyStore : Featly.IFeatlyStore
{
    /// <summary>Persistence operations on flags.</summary>
    IFlagStore Flags { get; }

    /// <summary>Persistence operations on projects.</summary>
    IProjectStore Projects { get; }

    /// <summary>Persistence operations on environments.</summary>
    IEnvironmentStore Environments { get; }

    /// <summary>Persistence operations on segments.</summary>
    ISegmentStore Segments { get; }

    /// <summary>Persistence operations on dynamic configs.</summary>
    IConfigStore Configs { get; }

    /// <summary>Persistence operations on users (M6+).</summary>
    IUserStore Users { get; }

    /// <summary>Persistence operations on roles (M6+).</summary>
    IRoleStore Roles { get; }

    /// <summary>In-process change notification stream feeding SSE.</summary>
    IChangeNotifier Changes { get; }
}
