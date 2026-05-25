namespace Featly;

/// <summary>
/// Marker interface for the storage facade. Real sub-store contracts and
/// the facade itself live in <c>Featly.Storage.Abstractions</c> so that
/// non-server consumers (the SDK) never pull in storage dependencies.
/// </summary>
/// <remarks>
/// Placeholder shape for M1. The full <c>IFeatlyStore</c> facade in
/// <c>Featly.Storage.Abstractions</c> aggregates 19 sub-stores. Defined
/// here so that pre-server reference types can mention it without
/// taking a hard dependency on the storage package.
/// </remarks>
public interface IFeatlyStore
{
}
