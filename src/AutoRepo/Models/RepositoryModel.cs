using System.Collections.Generic;

namespace AutoRepo.Models;

public sealed class RepositoryModel
{
    public string Name { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string RoutePrefix { get; set; } = string.Empty;
    public List<MethodModel> Methods { get; set; } = new();
    public string IdType { get; set; } = "int";

    /// <summary>
    /// Optional fully qualified type name of a static class with a "Default" property
    /// returning JsonSerializerOptions. If null, a sensible default is used.
    /// </summary>
    public string? JsonOptionsType { get; set; }
}

public sealed class MethodModel
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public string InnerReturnType { get; set; } = string.Empty;
    public bool ReturnsCollection { get; set; }
    public bool ReturnsNullable { get; set; }
    public bool HasSource { get; set; }
    public List<ParameterModel> Parameters { get; set; } = new();
    public HttpMethod HttpMethod { get; set; } = HttpMethod.Get;
}

public sealed class ParameterModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool HasDefaultValue { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsCancellationToken { get; set; }
    public bool IsRouteParameter { get; set; }
    public bool IsQueryParameter { get; set; }
    public bool IsBodyParameter { get; set; }
}

public enum HttpMethod
{
    Get,
    Post,
    Put,
    Delete,
    Patch
}
