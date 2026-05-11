using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllTailwindClassesGenerator;

internal static class V4Diff
{
    public static readonly string[] DiffableFiles =
    [
        "classes.json",
        "colors.json",
        "descriptions.json",
        "theme.json",
        "variants.json",
        "order.json",
        "variantorder.json"
    ];

    public static async Task Generate(string v1Folder, string v2Folder)
    {
        var diffFolder = Path.Combine(v2Folder, "diff");

        if (Directory.Exists(diffFolder))
        {
            Directory.Delete(diffFolder, true);
        }

        Directory.CreateDirectory(diffFolder);

        await GenerateKeyedArrayDiff(Path.Combine(v1Folder, "classes.json"), Path.Combine(v2Folder, "classes.json"), Path.Combine(diffFolder, "classes.json"), "s");
        await GenerateObjectDiff(Path.Combine(v1Folder, "colors.json"), Path.Combine(v2Folder, "colors.json"), Path.Combine(diffFolder, "colors.json"));
        await GenerateObjectDiff(Path.Combine(v1Folder, "descriptions.json"), Path.Combine(v2Folder, "descriptions.json"), Path.Combine(diffFolder, "descriptions.json"));
        await GenerateObjectDiff(Path.Combine(v1Folder, "theme.json"), Path.Combine(v2Folder, "theme.json"), Path.Combine(diffFolder, "theme.json"));
        await GenerateObjectDiff(Path.Combine(v1Folder, "variants.json"), Path.Combine(v2Folder, "variants.json"), Path.Combine(diffFolder, "variants.json"));
        await GenerateOrderDiff(Path.Combine(v1Folder, "order.json"), Path.Combine(v2Folder, "order.json"), Path.Combine(diffFolder, "order.json"));
        await GenerateOrderDiff(Path.Combine(v1Folder, "variantorder.json"), Path.Combine(v2Folder, "variantorder.json"), Path.Combine(diffFolder, "variantorder.json"));
    }

    public static async Task Revert(string currentFolder, string diffFolder, string outputFolder, string? originalFolder = null)
    {
        if (!Directory.Exists(currentFolder))
        {
            throw new DirectoryNotFoundException($"Current folder not found: {currentFolder}");
        }

        if (!Directory.Exists(diffFolder))
        {
            throw new DirectoryNotFoundException($"Diff folder not found: {diffFolder}");
        }

        Directory.CreateDirectory(outputFolder);

        foreach (var file in DiffableFiles)
        {
            var currentPath = Path.Combine(currentFolder, file);
            var diffPath = Path.Combine(diffFolder, file);
            var outputPath = Path.Combine(outputFolder, file);
            var originalPath = originalFolder is null ? null : Path.Combine(originalFolder, file);

            if (!File.Exists(diffPath))
            {
                File.Copy(currentPath, outputPath, true);
                continue;
            }

            if (file == "classes.json")
            {
                await RevertKeyedArrayDiff(currentPath, diffPath, outputPath, "s", originalPath);
            }
            else if (file == "order.json" || file == "variantorder.json")
            {
                await RevertOrderDiff(currentPath, diffPath, outputPath, originalPath);
            }
            else
            {
                await RevertObjectDiff(currentPath, diffPath, outputPath, originalPath);
            }
        }
    }

    private static async Task RevertObjectDiff(string currentPath, string diffPath, string outputPath, string? originalPath)
    {
        var current = await ReadObject(currentPath);
        var diff = await ReadObject(diffPath);
        JsonObject? original = null;

        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
        {
            original = await ReadObject(originalPath);
        }

        var result = (JsonObject)current.DeepClone();
        var add = diff["add"] as JsonObject ?? [];
        var remove = diff["remove"] as JsonObject ?? [];
        var @override = diff["override"] as JsonObject ?? [];

        foreach (var pair in add)
        {
            result.Remove(pair.Key);
        }

        foreach (var pair in remove)
        {
            result[pair.Key] = pair.Value?.DeepClone();
        }

        foreach (var pair in @override)
        {
            if (original?.TryGetPropertyValue(pair.Key, out var originalValue) == true)
            {
                result[pair.Key] = originalValue?.DeepClone();
            }
        }

        await WriteJson(outputPath, result);
    }

    private static async Task RevertKeyedArrayDiff(string currentPath, string diffPath, string outputPath, string keyProperty, string? originalPath)
    {
        var current = await ReadArray(currentPath);
        var diff = await ReadObject(diffPath);
        JsonArray? original = null;

        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
        {
            original = await ReadArray(originalPath);
        }

        var currentByKey = ToKeyedDictionary(current, keyProperty);
        var originalByKey = original is null
            ? new Dictionary<string, JsonObject>()
            : ToKeyedDictionary(original, keyProperty);

        var add = diff["add"] as JsonArray ?? [];
        var remove = diff["remove"] as JsonArray ?? [];
        var @override = diff["override"] as JsonArray ?? [];

        foreach (var node in add)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var key = obj[keyProperty]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(key))
            {
                currentByKey.Remove(key);
            }
        }

        foreach (var node in remove)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var key = obj[keyProperty]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(key))
            {
                currentByKey[key] = (JsonObject)obj.DeepClone();
            }
        }

        foreach (var node in @override)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var key = obj[keyProperty]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(key) && originalByKey.TryGetValue(key, out var originalObj))
            {
                currentByKey[key] = (JsonObject)originalObj.DeepClone();
            }
        }

        JsonArray reverted = [];
        foreach (var (_, value) in currentByKey.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            reverted.Add(value);
        }

        await WriteJsonArray(outputPath, reverted);
    }

    private static async Task RevertOrderDiff(string currentPath, string diffPath, string outputPath, string? originalPath)
    {
        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
        {
            // Restoring from the actual original order file is exact and avoids lossy reconstruction.
            var original = await ReadStringArray(originalPath);
            await WriteStringArray(outputPath, original);
            return;
        }

        var current = await ReadStringArray(currentPath);
        var diff = await ReadObject(diffPath);

        var additions = diff["add"] as JsonArray ?? [];
        var remove = diff["remove"] as JsonArray ?? [];

        var addedItems = additions
            .OfType<JsonObject>()
            .SelectMany(obj => obj.Select(pair => pair.Key))
            .ToHashSet();

        var result = current.Where(item => !addedItems.Contains(item)).ToList();
        var existing = result.ToHashSet();

        foreach (var item in remove.OfType<JsonValue>().Select(v => v.GetValue<string>()))
        {
            if (existing.Add(item))
            {
                result.Add(item);
            }
        }

        await WriteStringArray(outputPath, result);
    }

    private static async Task GenerateObjectDiff(string v1Path, string v2Path, string outputPath)
    {
        var v1 = await ReadObject(v1Path);
        var v2 = await ReadObject(v2Path);

        JsonObject add = [];
        JsonObject remove = [];
        JsonObject @override = [];

        foreach (var pair in v2)
        {
            if (!v1.TryGetPropertyValue(pair.Key, out var previousValue))
            {
                add[pair.Key] = pair.Value?.DeepClone();
                continue;
            }

            if (!JsonNode.DeepEquals(previousValue, pair.Value))
            {
                @override[pair.Key] = pair.Value?.DeepClone();
            }
        }

        foreach (var pair in v1)
        {
            if (!v2.ContainsKey(pair.Key))
            {
                remove[pair.Key] = pair.Value?.DeepClone();
            }
        }

        if (add.Count == 0 && remove.Count == 0 && @override.Count == 0)
        {
            // Avoid creating empty diff files
            return;
        }

        await WriteJson(outputPath, new JsonObject
        {
            ["add"] = add,
            ["remove"] = remove,
            ["override"] = @override
        });
    }

    private static async Task GenerateKeyedArrayDiff(string v1Path, string v2Path, string outputPath, string keyProperty)
    {
        var v1 = await ReadArray(v1Path);
        var v2 = await ReadArray(v2Path);

        var v1ByKey = ToKeyedDictionary(v1, keyProperty);
        var v2ByKey = ToKeyedDictionary(v2, keyProperty);

        JsonArray add = [];
        JsonArray remove = [];
        JsonArray @override = [];

        foreach (var pair in v2ByKey)
        {
            if (!v1ByKey.TryGetValue(pair.Key, out var previousValue))
            {
                add.Add(pair.Value.DeepClone());
                continue;
            }

            if (!JsonNode.DeepEquals(previousValue, pair.Value))
            {
                @override.Add(pair.Value.DeepClone());
            }
        }

        foreach (var pair in v1ByKey)
        {
            if (!v2ByKey.ContainsKey(pair.Key))
            {
                remove.Add(pair.Value.DeepClone());
            }
        }

        if (add.Count == 0 && remove.Count == 0 && @override.Count == 0)
        {
            // Avoid creating empty diff files
            return;
        }

        await WriteJson(outputPath, new JsonObject
        {
            ["add"] = add,
            ["remove"] = remove,
            ["override"] = @override
        });
    }

    private static async Task GenerateOrderDiff(string v1Path, string v2Path, string outputPath)
    {
        var v1 = await ReadStringArray(v1Path);
        var v2 = await ReadStringArray(v2Path);

        ValidateUnique(v1, v1Path);
        ValidateUnique(v2, v2Path);

        var v1Set = v1.ToHashSet();
        var v2Set = v2.ToHashSet();
        var fixedItems = GetStableOrderedItems(v1, v2);

        var movedItems = v1.Where(v2Set.Contains).Where(item => !fixedItems.Contains(item)).ToHashSet();

        JsonArray remove = [];
        foreach (var item in v1)
        {
            if (!v2Set.Contains(item) || movedItems.Contains(item))
            {
                remove.Add(item);
            }
        }

        JsonArray add = [];
        for (int i = 0; i < v2.Count; i++)
        {
            var item = v2[i];
            if (!v1Set.Contains(item) || movedItems.Contains(item))
            {
                add.Add(new JsonObject
                {
                    [item] = i
                });
            }
        }

        if (add.Count == 0 && remove.Count == 0)
        {
            // Avoid creating empty diff files
            return;
        }

        await WriteJson(outputPath, new JsonObject
        {
            ["add"] = add,
            ["remove"] = remove
        });
    }

    private static HashSet<string> GetStableOrderedItems(List<string> v1, List<string> v2)
    {
        var v2Positions = v2.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);
        var commonItems = v1.Where(v2Positions.ContainsKey).ToList();
        var positionSequence = commonItems.Select(item => v2Positions[item]).ToList();

        var stableIndices = GetLongestIncreasingSubsequenceIndices(positionSequence);

        return stableIndices.Select(index => commonItems[index]).ToHashSet();
    }

    private static List<int> GetLongestIncreasingSubsequenceIndices(List<int> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var tails = new List<int>();
        var previous = Enumerable.Repeat(-1, values.Count).ToArray();

        for (int i = 0; i < values.Count; i++)
        {
            int left = 0;
            int right = tails.Count;

            while (left < right)
            {
                var middle = left + (right - left) / 2;

                if (values[tails[middle]] < values[i])
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }

            if (left > 0)
            {
                previous[i] = tails[left - 1];
            }

            if (left == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[left] = i;
            }
        }

        var result = new List<int>();
        for (int current = tails[^1]; current >= 0; current = previous[current])
        {
            result.Add(current);
        }

        result.Reverse();
        return result;
    }

    private static async Task<JsonObject> ReadObject(string path)
    {
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream) as JsonObject)
            ?? throw new InvalidDataException($"Expected {path} to contain a JSON object.");
    }

    private static async Task<JsonArray> ReadArray(string path)
    {
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream) as JsonArray)
            ?? throw new InvalidDataException($"Expected {path} to contain a JSON array.");
    }

    private static async Task<List<string>> ReadStringArray(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<string>>(stream)
            ?? throw new InvalidDataException($"Expected {path} to contain a JSON string array.");
    }

    private static Dictionary<string, JsonObject> ToKeyedDictionary(JsonArray array, string keyProperty)
    {
        Dictionary<string, JsonObject> result = [];

        foreach (var node in array)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidDataException("Expected a JSON object entry.");
            }

            var key = obj[keyProperty]?.GetValue<string>()
                ?? throw new InvalidDataException($"Expected property '{keyProperty}' to be present.");

            result[key] = obj;
        }

        return result;
    }

    private static void ValidateUnique(List<string> values, string path)
    {
        HashSet<string> seen = [];

        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new InvalidDataException($"{path} must not contain duplicate entries. Found duplicate: {value}");
            }
        }
    }

    private static async Task WriteJson(string path, JsonObject value)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, value);
    }

    private static async Task WriteJsonArray(string path, JsonArray value)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, value);
    }

    private static async Task WriteStringArray(string path, List<string> value)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, value);
    }
}
