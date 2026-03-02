# AutoRepo

A C# source generator that automatically creates API client code from repository interfaces.

## What it generates

Given a repository interface annotated with `[GenerateApi]`, AutoRepo generates:

1. **`IApi{Name}`** - Refit-compatible interface with HTTP method attributes
2. **`Api{Name}Client`** - HttpClient-based implementation
3. **`Fallback{Name}`** - Local-first wrapper with API fallback
4. **`Combined{Name}`** - Merged wrapper that queries both local and remote sources in parallel

## Usage

Add AutoRepo as an analyzer reference:

```xml
<ProjectReference Include="path/to/AutoRepo.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Annotate your repository interfaces:

```csharp
using AutoRepo;

[GenerateApi(RoutePrefix = "/api/products")]
public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);
    Task<Product?> GetAsync(int id, CancellationToken ct = default);
    Task UpsertAsync(Product product, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
```

### Custom JSON serialization

If your entities need custom JSON converters (e.g., for strongly-typed IDs), specify a `JsonOptionsType`:

```csharp
[GenerateApi(RoutePrefix = "/api/products", JsonOptionsType = "MyApp.MyJsonOptions")]
public interface IProductRepository { ... }
```

The type must have a static `Default` property returning `JsonSerializerOptions`.

## Conventions

- **HTTP method inference**: `Get*`/`Search*`/`Find*` -> GET, `Delete*`/`Remove*` -> DELETE, `Create*`/`Add*` -> POST, `Update*` -> PUT, `Upsert*` -> POST (with body) or PUT
- **Route parameters**: Parameters named `id` or ending with `Id` in Get/Delete methods
- **Body parameters**: Complex types in POST/PUT methods
- **Query parameters**: Primitive types that aren't route parameters
- **Source tracking**: If entities have a `Source` property of type `DataSource`, generated wrappers set it to `Local`, `Remote`, or `Both`
- **Fallback strategy**: Single items -> local-first; Collections -> API-first; Mutations -> local-only
- **Combined strategy**: All reads query both sources in parallel and merge; Mutations -> local-only
- **Local-only methods**: Methods starting with `GetStored` or `GetLibrary` stay local-only in Combined wrapper

## Requirements

- .NET SDK with Roslyn source generators support
- [Refit](https://github.com/reactiveui/refit) (for the generated Refit interface)
- `Microsoft.Extensions.Logging` (for Fallback/Combined wrappers)
