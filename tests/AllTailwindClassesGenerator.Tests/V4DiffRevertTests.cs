using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllTailwindClassesGenerator.Tests;

public class V4DiffRevertTests
{
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
            await V4Diff.Revert(originalDirectory, Path.Combine(modifiedDirectory, "diff"), revertedDirectory);

            foreach (var file in V4Diff.DiffableFiles)
            {
                var expected = await ReadJson(Path.Combine(modifiedDirectory, file));
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
        // V4 generation currently shells out through `cmd` in production code paths.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRootDirectory = Path.Combine(Path.GetTempPath(), $"v4diff-real-{Guid.NewGuid():N}");
        var originalDirectory = Path.Combine(testRootDirectory, "v4_0_17");
        var modifiedDirectory = Path.Combine(testRootDirectory, "v4_3_0");
        var revertedDirectory = Path.Combine(testRootDirectory, "reverted");
        var workspaceRoot = Path.Combine(testRootDirectory, "workspace");
        var workspaceWorkingDirectory = Path.Combine(workspaceRoot, "bin", "Debug", "net10.0");
        var workspacev4Directory = Path.Combine(workspaceRoot, "v4");
        var previousCurrentDirectory = Environment.CurrentDirectory;

        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(modifiedDirectory);
        Directory.CreateDirectory(workspaceWorkingDirectory);
        Directory.CreateDirectory(workspacev4Directory);

        try
        {
            var repositoryRoot = GetRepositoryRoot();
            CopyProgramInputs(repositoryRoot, workspaceRoot);

            Environment.CurrentDirectory = workspaceWorkingDirectory;

            await GenerateVersionOutputs("4.0.17", originalDirectory);
            await GenerateVersionOutputs("4.3.0", modifiedDirectory);

            await V4Diff.Generate(originalDirectory, modifiedDirectory);
            await V4Diff.Revert(originalDirectory, Path.Combine(modifiedDirectory, "diff"), revertedDirectory);

            foreach (var file in V4Diff.DiffableFiles)
            {
                var expected = await ReadJson(Path.Combine(modifiedDirectory, file));
                var actual = await ReadJson(Path.Combine(revertedDirectory, file));

                Assert.True(
                    JsonNode.DeepEquals(Normalize(file, expected), Normalize(file, actual)),
                    $"Mismatch after revert for {file}");
            }
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;

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

    private static async Task GenerateVersionOutputs(string version, string outputDirectory)
    {
        await InstallTailwindVersion(version);

        var versionTag = $"v{version}";
        await V4.GenerateClassesFromV3(versionTag);
        await V4.CompileClasses();
        await V4.ExtractClassesAndDescriptions(false);
        await V4.ExtractDefaultTheme();
        await V4.ExtractVariants(versionTag);
        await V4.GetSortOrder();
        await V4.GetVariantSortOrder();

        Directory.CreateDirectory(outputDirectory);
        foreach (var file in V4Diff.DiffableFiles)
        {
            File.Copy(Path.Combine(Helpers.V4Folder, file), Path.Combine(outputDirectory, file), true);
        }
    }

    private static async Task InstallTailwindVersion(string version)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("cmd")
        {
            WorkingDirectory = Helpers.BaseFolder,
            Arguments = $"/c npm install @tailwindcss/cli@{version} tailwindcss@{version}"
        };

        process.Start();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
    }

    private static void CopyProgramInputs(string repositoryRoot, string workspaceRoot)
    {
        File.Copy(Path.Combine(repositoryRoot, "tailwindclasses-base.json"), Path.Combine(workspaceRoot, "tailwindclasses-base.json"), true);
        File.Copy(Path.Combine(repositoryRoot, "v4-variants.txt"), Path.Combine(workspaceRoot, "v4-variants.txt"), true);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AllTailwindClassesGenerator.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static async Task WriteJson(string path, JsonNode node)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, node);
    }
}
