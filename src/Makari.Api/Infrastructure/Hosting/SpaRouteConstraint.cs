namespace Makari.Api.Infrastructure.Hosting;

// Exclude server namespaces from route matching, not just from the fallback response.
// This preserves API 404/405 behavior and prevents an HTML response for API requests.
public sealed class SpaRouteConstraint : IRouteConstraint
{
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        var path = new PathString("/" + Convert.ToString(values[routeKey])?.TrimStart('/'));
        return !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
    }
}
