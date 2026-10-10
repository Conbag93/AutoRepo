using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoRepo.Tests;

/// <summary>
/// Runs <see cref="RepositoryGenerator"/> over a small consumer project, compiles the result together with
/// hand-written fakes for the local repository and the generated IApi interface, and loads it so the
/// generated Fallback/Combined wrappers can be exercised for real rather than string-matched.
/// </summary>
internal static class GeneratorHarness
{
    /// <summary>
    /// A widget entity, a fake local repository and a fake API client whose answers each test controls.
    /// <c>{ATTRS}</c> is replaced with interface-level attributes, <c>{SINGLE_ATTRS}</c> with attributes on
    /// the single-item read.
    /// </summary>
    private const string ConsumerTemplate = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        using AutoRepo;
        using Demo.Abstractions;
        using Demo.Abstractions.Generated;
        using Demo.Entities;
        using Demo.Repositories;
        using Microsoft.Extensions.Logging.Abstractions;

        namespace Demo.Entities
        {
            public enum DataSource { Local, Remote, Both }

            public class Widget
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public DataSource? Source { get; set; }
            }
        }

        namespace Demo.Abstractions
        {
            [GenerateApi(RoutePrefix = "/api/widgets")]
            {ATTRS}
            public interface IWidgetRepository
            {
                {SINGLE_ATTRS}
                Task<Widget?> GetByIdAsync(int id, CancellationToken ct = default);

                Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken ct = default);
            }
        }

        namespace Demo.Repositories
        {
            public class WidgetRepository : IWidgetRepository
            {
                public Widget? Single { get; set; }
                public List<Widget> Many { get; set; } = new();

                public Task<Widget?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Single);
                public Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Widget>>(Many);
            }
        }

        namespace Demo
        {
            public class FakeApi : IApiWidgetRepository
            {
                public Widget? Single { get; set; }
                public List<Widget> Many { get; set; } = new();
                public bool Throws { get; set; }

                public Task<Widget?> GetByIdAsync(int id, CancellationToken ct = default)
                    => Throws ? throw new InvalidOperationException("api down") : Task.FromResult(Single);

                public Task<IReadOnlyList<Widget>> GetAllAsync(CancellationToken ct = default)
                    => Throws ? throw new InvalidOperationException("api down") : Task.FromResult<IReadOnlyList<Widget>>(Many);
            }

            /// <summary>Entry points the tests call by reflection. Rows are "id:name" strings.</summary>
            public static class Scenario
            {
                public static async Task<string> Single(string wrapper, string? local, string? api, bool apiThrows)
                {
                    var (repo, _, _) = Build(wrapper, local, api, apiThrows, null, null);
                    var w = await repo.GetByIdAsync(1);
                    return w is null ? "null" : $"{w.Name}|{w.Source}";
                }

                public static async Task<string> Many(string wrapper, string[] local, string[] api, bool apiThrows)
                {
                    var (repo, _, _) = Build(wrapper, null, null, apiThrows, local, api);
                    var items = await repo.GetAllAsync();
                    return string.Join(",", items.Select(w => $"{w.Id}:{w.Name}|{w.Source}"));
                }

                private static (IWidgetRepository, WidgetRepository, FakeApi) Build(
                    string wrapper, string? local, string? api, bool apiThrows, string[]? localMany, string[]? apiMany)
                {
                    var localRepo = new WidgetRepository
                    {
                        Single = local is null ? null : new Widget { Id = 1, Name = local },
                        Many = (localMany ?? Array.Empty<string>()).Select(Parse).ToList()
                    };
                    var fakeApi = new FakeApi
                    {
                        Single = api is null ? null : new Widget { Id = 1, Name = api },
                        Many = (apiMany ?? Array.Empty<string>()).Select(Parse).ToList(),
                        Throws = apiThrows
                    };
                    IWidgetRepository repo = wrapper == "Combined"
                        ? new CombinedWidgetRepository(localRepo, fakeApi, NullLogger<CombinedWidgetRepository>.Instance)
                        : new FallbackWidgetRepository(localRepo, fakeApi, NullLogger<FallbackWidgetRepository>.Instance);
                    return (repo, localRepo, fakeApi);
                }

                private static Widget Parse(string s)
                {
                    var parts = s.Split(':');
                    return new Widget { Id = int.Parse(parts[0]), Name = parts[1] };
                }
            }
        }
        """;

    public sealed record Generated(Assembly Assembly, IReadOnlyDictionary<string, string> Sources)
    {
        public async Task<string> Single(string wrapper, string? local, string? api, bool apiThrows = false)
            => await Invoke<string>("Single", wrapper, local, api, apiThrows);

        public async Task<string> Many(string wrapper, string[] local, string[] api, bool apiThrows = false)
            => await Invoke<string>("Many", wrapper, local, api, apiThrows);

        private Task<T> Invoke<T>(string method, params object?[] args)
        {
            var scenario = Assembly.GetType("Demo.Scenario", throwOnError: true)!;
            return (Task<T>)scenario.GetMethod(method)!.Invoke(null, args)!;
        }
    }

    public static Generated Run(string interfaceAttributes = "", string singleItemAttributes = "")
    {
        var source = ConsumerTemplate
            .Replace("{ATTRS}", interfaceAttributes)
            .Replace("{SINGLE_ATTRS}", singleItemAttributes);

        var compilation = CSharpCompilation.Create(
            "AutoRepoGeneratorTest_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RepositoryGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics);

        var sources = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString());

        using var pe = new MemoryStream();
        var emit = output.Emit(pe);
        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(emit.Success, "Generated code failed to compile:\n" + string.Join("\n", errors));

        pe.Position = 0;
        var alc = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        return new Generated(alc.LoadFromStream(pe), sources);
    }

    private static IEnumerable<MetadataReference> References()
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return tpa
            .Concat(new[]
            {
                typeof(Refit.GetAttribute).Assembly.Location,
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location,
                typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger).Assembly.Location
            })
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
    }
}
