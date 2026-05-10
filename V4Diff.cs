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
                    ["value"] = item,
                    ["index"] = i
                });
            }
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
                var middle = (left + right) / 2;

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
        if (values.Count != values.Distinct().Count())
        {
            throw new InvalidDataException($"{path} must not contain duplicate entries.");
        }
    }

    private static async Task WriteJson(string path, JsonObject value)
    {
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await JsonSerializer.SerializeAsync(stream, value);
    }
}
