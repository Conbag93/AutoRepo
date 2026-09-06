using System.Collections.Generic;
using System.Linq;
using AutoRepo.Models;

namespace AutoRepo.Emitters;

/// <summary>
/// Shared route-construction logic used by both RefitEmitter and HttpClientEmitter,
/// so the two generated clients can never disagree about a method's URL.
/// </summary>
internal static class RouteBuilder
{
    public static string Build(string routePrefix, MethodModel method)
    {
        var prefix = routePrefix.TrimEnd('/');

        // [ApiRoute("...")] wins outright — the generator stops guessing entirely.
        // An empty route means "the bare prefix, on purpose" (e.g. a plain GET /api/things
        // sibling to other explicitly-routed methods on the same resource).
        if (method.ExplicitRoute != null)
        {
            var explicitRoute = method.ExplicitRoute.Trim('/');
            return explicitRoute.Length == 0 ? prefix : $"{prefix}/{explicitRoute}";
        }

        var parts = new List<string> { prefix };

        foreach (var param in method.Parameters.Where(p => p.IsRouteParameter))
        {
            parts.Add($"{{{param.Name}}}");
        }

        if (method.Name.StartsWith("Search"))
        {
            if (method.Parameters.Any(p => p.IsRouteParameter))
                parts.Insert(1, "search");
            else
                parts.Add("search");
        }

        return string.Join("/", parts);
    }
}
