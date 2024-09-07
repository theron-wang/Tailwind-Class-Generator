using System.Text.Json.Serialization;

namespace AllTailwindClassesGenerator;

public class Subvariant
{
    [JsonPropertyName("ss")]
    public required string Stem { get; set; }
    [JsonPropertyName("v")]

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Variants { get; set; }
}