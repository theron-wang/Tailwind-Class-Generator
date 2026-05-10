using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllTailwindClassesGenerator.Tests;

public class V4DiffRevertTests
{
    [Fact]
    public async Task Revert_Restores_Original_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"v4diff-test-{Guid.NewGuid():N}");
        var v1 = Path.Combine(root, "v1");
        var v2 = Path.Combine(root, "v2");
        var reverted = Path.Combine(root, "reverted");

        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);

        try
        {
            await WriteFixtures(v1, v2);

            await V4Diff.Generate(v1, v2);
            await V4Diff.Revert(v2, Path.Combine(v2, "diff"), reverted);

            foreach (var file in V4Diff.DiffableFiles)
            {
                var expected = await ReadJson(Path.Combine(v1, file));
                var actual = await ReadJson(Path.Combine(reverted, file));

                Assert.True(
                    JsonNode.DeepEquals(Normalize(file, expected), Normalize(file, actual)),
                    $"Mismatch after revert for {file}");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static JsonNode? Normalize(string fileName, JsonNode? node)
    {
        if (fileName != "classes.json" || node is not JsonArray array)
        {
            return node;
        }

        var ordered = array
            .OfType<JsonObject>()
            .OrderBy(o => o["s"]?.GetValue<string>(), StringComparer.Ordinal)
            .Select(o => o.DeepClone())
            .ToArray();

        JsonArray normalized = [];
        foreach (var item in ordered)
        {
            normalized.Add(item);
        }

        return normalized;
    }

    private static async Task<JsonNode?> ReadJson(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream);
    }

    private static async Task WriteFixtures(string v1, string v2)
    {
        await WriteJson(Path.Combine(v1, "classes.json"), JsonNode.Parse("""
            [
              { "s": "alpha", "dv": ["1"] },
              { "s": "beta" }
            ]
            """)!);
        await WriteJson(Path.Combine(v2, "classes.json"), JsonNode.Parse("""
            [
              { "s": "alpha", "dv": ["2"] },
              { "s": "gamma" }
            ]
            """)!);

        await WriteJson(Path.Combine(v1, "colors.json"), JsonNode.Parse("""{ "a": "1", "b": "2" }""")!);
        await WriteJson(Path.Combine(v2, "colors.json"), JsonNode.Parse("""{ "a": "3", "c": "4" }""")!);

        await WriteJson(Path.Combine(v1, "descriptions.json"), JsonNode.Parse("""{ "x": "first", "y": "same" }""")!);
        await WriteJson(Path.Combine(v2, "descriptions.json"), JsonNode.Parse("""{ "x": "changed", "z": "new", "y": "same" }""")!);

        await WriteJson(Path.Combine(v1, "theme.json"), JsonNode.Parse("""{ "k1": "v1", "k2": "v2" }""")!);
        await WriteJson(Path.Combine(v2, "theme.json"), JsonNode.Parse("""{ "k1": "v1-updated", "k3": "v3" }""")!);

        await WriteJson(Path.Combine(v1, "variants.json"), JsonNode.Parse("""{ "hover": "h", "focus": "f" }""")!);
        await WriteJson(Path.Combine(v2, "variants.json"), JsonNode.Parse("""{ "hover": "h2", "active": "a" }""")!);

        await WriteJson(Path.Combine(v1, "order.json"), JsonNode.Parse("""["a","x","b","c"]""")!);
        await WriteJson(Path.Combine(v2, "order.json"), JsonNode.Parse("""["c","a","b","z"]""")!);

        await WriteJson(Path.Combine(v1, "variantorder.json"), JsonNode.Parse("""["sm","hover","focus"]""")!);
        await WriteJson(Path.Combine(v2, "variantorder.json"), JsonNode.Parse("""["focus","sm","print"]""")!);
    }

    private static async Task WriteJson(string path, JsonNode node)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, node);
    }
}
