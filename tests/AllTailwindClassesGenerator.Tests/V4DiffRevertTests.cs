using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AllTailwindClassesGenerator.Tests;

public class V4DiffRevertTests
{
    private static readonly HttpClient HttpClient = new();

    [Fact]
    public async Task Revert_Restores_Original_Files()
    {
        var testRootDirectory = Path.Combine(Path.GetTempPath(), $"v4diff-test-{Guid.NewGuid():N}");
        var originalDirectory = Path.Combine(testRootDirectory, "v1");
        var modifiedDirectory = Path.Combine(testRootDirectory, "v2");
        var revertedDirectory = Path.Combine(testRootDirectory, "reverted");

        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(modifiedDirectory);

        try
        {
            await WriteFixtures(originalDirectory, modifiedDirectory);

            await V4Diff.Generate(originalDirectory, modifiedDirectory);
            await V4Diff.Revert(modifiedDirectory, Path.Combine(modifiedDirectory, "diff"), revertedDirectory, originalDirectory);

            foreach (var file in V4Diff.DiffableFiles)
            {
                var expected = await ReadJson(Path.Combine(originalDirectory, file));
                var actual = await ReadJson(Path.Combine(revertedDirectory, file));

                Assert.True(
                    JsonNode.DeepEquals(Normalize(file, expected), Normalize(file, actual)),
                    $"Mismatch after revert for {file}");
            }
        }
        finally
        {
            if (Directory.Exists(testRootDirectory))
            {
                Directory.Delete(testRootDirectory, true);
            }
        }
    }

    [Fact]
    public async Task Revert_Restores_Original_Files_With_Actual_Versions_4_0_17_And_4_3_0()
    {
        var testRootDirectory = Path.Combine(Path.GetTempPath(), $"v4diff-real-{Guid.NewGuid():N}");
        var originalDirectory = Path.Combine(testRootDirectory, "v4.0.17");
        var modifiedDirectory = Path.Combine(testRootDirectory, "v4.3.0");
        var revertedDirectory = Path.Combine(testRootDirectory, "reverted");

        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(modifiedDirectory);

        try
        {
            await WriteVersionedFixtures("v4.0.17", originalDirectory);
            await WriteVersionedFixtures("v4.3.0", modifiedDirectory);

            await V4Diff.Generate(originalDirectory, modifiedDirectory);
            await V4Diff.Revert(modifiedDirectory, Path.Combine(modifiedDirectory, "diff"), revertedDirectory, originalDirectory);

            foreach (var file in V4Diff.DiffableFiles)
            {
                var expected = await ReadJson(Path.Combine(originalDirectory, file));
                var actual = await ReadJson(Path.Combine(revertedDirectory, file));

                Assert.True(
                    JsonNode.DeepEquals(Normalize(file, expected), Normalize(file, actual)),
                    $"Mismatch after revert for {file}");
            }
        }
        finally
        {
            if (Directory.Exists(testRootDirectory))
            {
                Directory.Delete(testRootDirectory, true);
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

    private static async Task WriteVersionedFixtures(string versionTag, string outputDirectory)
    {
        var snapshot = await DownloadIntellisenseSnapshot(versionTag);
        var classes = ParseClassList(snapshot);
        var variants = ParseVariantNames(snapshot);

        var classVariants = classes
            .Take(200)
            .Select(c => new JsonObject
            {
                ["s"] = c
            })
            .ToArray();

        JsonArray classesJson = [];
        foreach (var item in classVariants)
        {
            classesJson.Add(item);
        }

        var colors = new JsonObject
        {
            ["black"] = "rgb(0 0 0 / 1)",
            ["seed"] = classes.FirstOrDefault() ?? "none",
            ["count"] = classes.Count.ToString()
        };

        var descriptions = new JsonObject
        {
            ["sample"] = string.Join(' ', classes.Take(8)),
            ["version"] = versionTag
        };

        var theme = new JsonObject
        {
            ["--spacing"] = classes.Count.ToString(),
            ["--radius"] = variants.Count.ToString()
        };

        JsonObject variantsJson = [];
        foreach (var variant in variants.Take(200))
        {
            variantsJson[variant] = variant;
        }

        JsonArray order = [];
        foreach (var name in classes.Take(300))
        {
            order.Add(name);
        }

        JsonArray variantOrder = [];
        foreach (var name in variants.Take(300))
        {
            variantOrder.Add(name);
        }

        await WriteJson(Path.Combine(outputDirectory, "classes.json"), classesJson);
        await WriteJson(Path.Combine(outputDirectory, "colors.json"), colors);
        await WriteJson(Path.Combine(outputDirectory, "descriptions.json"), descriptions);
        await WriteJson(Path.Combine(outputDirectory, "theme.json"), theme);
        await WriteJson(Path.Combine(outputDirectory, "variants.json"), variantsJson);
        await WriteJson(Path.Combine(outputDirectory, "order.json"), order);
        await WriteJson(Path.Combine(outputDirectory, "variantorder.json"), variantOrder);
    }

    private static async Task<string> DownloadIntellisenseSnapshot(string versionTag)
    {
        using var response = await HttpClient.GetAsync($"https://raw.githubusercontent.com/tailwindlabs/tailwindcss/{versionTag}/packages/tailwindcss/src/__snapshots__/intellisense.test.ts.snap");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static List<string> ParseClassList(string snapshot)
    {
        var section = ExtractSnapshotSection(snapshot, "getClassList");
        if (section is null)
        {
            return [];
        }

        // Tailwind snapshots are JS-like and can include trailing commas in arrays;
        // normalize to strict JSON before deserializing.
        var normalized = Regex.Replace(section, @",\s*\]", "]");
        return JsonSerializer.Deserialize<List<string>>(normalized) ?? [];
    }

    private static List<string> ParseVariantNames(string snapshot)
    {
        var section = ExtractSnapshotSection(snapshot, "getVariants");
        if (section is null)
        {
            return [];
        }

        HashSet<string> variants = [];

        foreach (Match match in Regex.Matches(section, "\"name\"\\s*:\\s*\"(?<name>[^\"]+)\""))
        {
            variants.Add(match.Groups["name"].Value.Trim());
        }

        return [.. variants];
    }

    private static string? ExtractSnapshotSection(string snapshot, string sectionName)
    {
        var marker = $"exports[`{sectionName} 1`] = `";
        var markerIndex = snapshot.IndexOf(marker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return null;
        }

        var start = snapshot.IndexOf('\n', markerIndex);
        if (start < 0)
        {
            return null;
        }

        start++;
        var end = snapshot.IndexOf("\n`", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        return snapshot[start..end].Trim();
    }

    private static async Task WriteJson(string path, JsonNode node)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, node);
    }
}
