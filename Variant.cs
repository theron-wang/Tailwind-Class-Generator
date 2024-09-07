using System.Text.Json.Serialization;

namespace AllTailwindClassesGenerator;

public class Variant
{
    [JsonPropertyName("s")]
    public required string Stem { get; set; }

    [JsonPropertyName("svs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Subvariant>? Subvariants { get; set; }

    [JsonPropertyName("dv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DirectVariants { get; set; }

    [JsonPropertyName("c")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseColors { get; set; }

    [JsonPropertyName("o")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseOpacity { get; set; }
    
    [JsonPropertyName("n")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasNegative { get; set; }

    [JsonPropertyName("sp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseSpacing { get; set; }
}
