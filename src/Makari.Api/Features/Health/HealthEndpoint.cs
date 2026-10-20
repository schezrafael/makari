namespace Makari.Api.Features.Health;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("Health")
            .WithTags("Health");
        return endpoints;
    }
}
