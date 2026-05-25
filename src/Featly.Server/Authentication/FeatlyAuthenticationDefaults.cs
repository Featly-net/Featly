namespace Featly.Server.Authentication;

/// <summary>Authentication scheme names and constants used by Featly server.</summary>
public static class FeatlyAuthenticationDefaults
{
    /// <summary>Scheme name for the admin API bearer token.</summary>
    public const string AdminScheme = "FeatlyAdmin";

    /// <summary>Scheme name for the SDK API bearer token.</summary>
    public const string SdkScheme = "FeatlySdk";

    /// <summary>Claim type identifying which API key scope authenticated the request.</summary>
    public const string ScopeClaim = "featly:scope";

    /// <summary>Authorization policy name protecting <c>/api/admin/*</c> endpoints.</summary>
    public const string AdminPolicy = "Featly.Admin";

    /// <summary>Authorization policy name protecting <c>/api/sdk/*</c> endpoints.</summary>
    public const string SdkPolicy = "Featly.Sdk";
}
