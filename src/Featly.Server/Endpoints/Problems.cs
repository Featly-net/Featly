using Microsoft.AspNetCore.Http;

namespace Featly.Server.Endpoints;

/// <summary>
/// Builds RFC 7807 <c>application/problem+json</c> responses so every Featly API
/// error has the same shape (issue #226). Field-level validation failures use
/// <see cref="Validation(string, string)"/> (RFC 7807 <c>ValidationProblemDetails</c>), giving
/// callers a uniform <c>errors</c> map (issue #230). Prefer these helpers over
/// returning ad-hoc <c>{ error }</c> anonymous objects.
/// </summary>
internal static class Problems
{
    /// <summary>404 — the addressed resource does not exist.</summary>
    public static IResult NotFound(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    /// <summary>400 — the request is malformed or violates a precondition.</summary>
    public static IResult BadRequest(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");

    /// <summary>409 — the request conflicts with the current state of the resource.</summary>
    public static IResult Conflict(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    /// <summary>403 — the caller is authenticated but not permitted.</summary>
    public static IResult Forbidden(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

    /// <summary>422 — the request is well-formed but semantically invalid.</summary>
    public static IResult UnprocessableEntity(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Unprocessable Entity");

    /// <summary>
    /// 400 with a single field error — the common "X is required / must be Y"
    /// case, surfaced as an RFC 7807 <c>ValidationProblemDetails</c> so the
    /// <c>errors</c> map is uniform across the API (issue #230).
    /// </summary>
    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>400 with several field errors as an RFC 7807 validation problem.</summary>
    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors);
}
