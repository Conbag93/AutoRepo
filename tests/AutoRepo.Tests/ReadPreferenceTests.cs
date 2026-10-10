using Xunit;

namespace AutoRepo.Tests;

/// <summary>
/// [ReadPreference] decides which source wins when a Fallback/Combined read finds data in both.
/// Default (no attribute) must stay local-first so client-owned data (a user's library) is unaffected.
/// </summary>
public class ReadPreferenceTests
{
    private const string ApiFirst = "[ReadPreference(ReadSource.ApiFirst)]";
    private const string LocalFirst = "[ReadPreference(ReadSource.LocalFirst)]";

    // ---- Combined, single item -------------------------------------------------------------

    [Fact]
    public async Task Combined_single_default_prefers_local_on_conflict()
    {
        var gen = GeneratorHarness.Run();
        Assert.Equal("stale-local|Both", await gen.Single("Combined", local: "stale-local", api: "server"));
    }

    [Fact]
    public async Task Combined_single_api_first_returns_api_row_on_conflict()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("server|Both", await gen.Single("Combined", local: "stale-local", api: "server"));
    }

    [Fact]
    public async Task Combined_single_api_first_returns_local_when_api_throws()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("stale-local|Local", await gen.Single("Combined", local: "stale-local", api: "server", apiThrows: true));
    }

    [Fact]
    public async Task Combined_single_api_first_returns_local_when_api_has_no_row()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("offline-write|Local", await gen.Single("Combined", local: "offline-write", api: null));
    }

    [Fact]
    public async Task Combined_single_api_first_returns_remote_only_row()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("server|Remote", await gen.Single("Combined", local: null, api: "server"));
    }

    [Fact]
    public async Task Combined_single_api_first_returns_null_when_neither_has_a_row()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("null", await gen.Single("Combined", local: null, api: null));
    }

    [Fact]
    public async Task Method_level_preference_overrides_interface()
    {
        var gen = GeneratorHarness.Run(interfaceAttributes: ApiFirst, singleItemAttributes: LocalFirst);
        Assert.Equal("stale-local|Both", await gen.Single("Combined", local: "stale-local", api: "server"));
    }

    [Fact]
    public async Task Method_level_api_first_without_interface_attribute()
    {
        var gen = GeneratorHarness.Run(singleItemAttributes: ApiFirst);
        Assert.Equal("server|Both", await gen.Single("Combined", local: "stale-local", api: "server"));
        // The collection read on the same interface keeps the default merge.
        Assert.Equal("1:local|Both", await gen.Many("Combined", local: ["1:local"], api: ["1:server"]));
    }

    // ---- Combined, collections -------------------------------------------------------------

    [Fact]
    public async Task Combined_collection_default_merges_by_id_preferring_local()
    {
        var gen = GeneratorHarness.Run();
        var result = await gen.Many("Combined", local: ["1:local", "3:local-only"], api: ["1:server", "2:server-only"]);
        Assert.Equal("1:local|Both,2:server-only|Remote,3:local-only|Local", result);
    }

    [Fact]
    public async Task Combined_collection_api_first_returns_api_list_as_is()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        // Local mirror rows can carry different ids for the same record, so they must not be merged in;
        // API order (e.g. LastUpdatedAt desc) must survive.
        var result = await gen.Many("Combined", local: ["9:stale-mirror", "1:local"], api: ["2:newest", "1:server"]);
        Assert.Equal("2:newest|Remote,1:server|Remote", result);
    }

    [Fact]
    public async Task Combined_collection_api_first_empty_api_list_is_authoritative()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("", await gen.Many("Combined", local: ["1:stale"], api: []));
    }

    [Fact]
    public async Task Combined_collection_api_first_returns_local_when_api_throws()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        var result = await gen.Many("Combined", local: ["1:local", "3:local"], api: ["1:server"], apiThrows: true);
        Assert.Equal("1:local|Local,3:local|Local", result);
    }

    // ---- Fallback, single item -------------------------------------------------------------

    [Fact]
    public async Task Fallback_single_default_is_local_first()
    {
        var gen = GeneratorHarness.Run();
        Assert.Equal("stale-local|Local", await gen.Single("Fallback", local: "stale-local", api: "server"));
    }

    [Fact]
    public async Task Fallback_single_api_first_returns_api_row()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("server|Remote", await gen.Single("Fallback", local: "stale-local", api: "server"));
    }

    [Fact]
    public async Task Fallback_single_api_first_falls_back_to_local_on_failure_or_miss()
    {
        var gen = GeneratorHarness.Run(ApiFirst);
        Assert.Equal("stale-local|Local", await gen.Single("Fallback", local: "stale-local", api: "server", apiThrows: true));
        Assert.Equal("stale-local|Local", await gen.Single("Fallback", local: "stale-local", api: null));
    }

    [Fact]
    public async Task Explicit_fallback_read_mode_stays_local_first_under_api_first()
    {
        var gen = GeneratorHarness.Run(ApiFirst, "[ReadOperation(Mode = ReadMode.Fallback)]");
        Assert.Equal("stale-local|Local", await gen.Single("Fallback", local: "stale-local", api: "server"));
        Assert.Equal("stale-local|Local", await gen.Single("Combined", local: "stale-local", api: "server"));
    }

    // ---- Attribute surface -----------------------------------------------------------------

    [Fact]
    public void ReadPreference_attribute_is_emitted_for_consumers()
    {
        var gen = GeneratorHarness.Run();
        var attrs = gen.Sources["OperationAttributes.g.cs"];
        Assert.Contains("public enum ReadSource", attrs);
        Assert.Contains("public class ReadPreferenceAttribute", attrs);
        Assert.Contains("AttributeTargets.Interface | System.AttributeTargets.Method", attrs);
    }
}
