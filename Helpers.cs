namespace AllTailwindClassesGenerator;
public static class Helpers
{
    public static readonly string BaseFolder;
    public static readonly string V4Folder;
    public static readonly string OutputPath;
    public static readonly string SpacingOutputPath;
    public static readonly string TailwindRgbOutputPath;
    public static readonly string OpacityOutputPath;
    public static readonly string CssOutputPath;
    public static readonly string OrderOutputPath;
    public static readonly string ModifiersOrderOutputPath;
    public static readonly string ModifiersPath;
    public static readonly string TestHtmlPath;

    static Helpers()
    {
        BaseFolder = Path.GetFullPath("../../../");
        V4Folder = Path.Combine(BaseFolder, "v4");
        OutputPath = Path.Combine(BaseFolder, "tailwindclasses.json");
        SpacingOutputPath = Path.Combine(BaseFolder, "tailwindspacing.json");
        TailwindRgbOutputPath = Path.Combine(BaseFolder, "tailwindrgbmapper.json");
        OpacityOutputPath = Path.Combine(BaseFolder, "tailwindopacity.json");
        CssOutputPath = Path.Combine(BaseFolder, "tailwinddesc.json");
        OrderOutputPath = Path.Combine(BaseFolder, "tailwindorder.json");
        ModifiersOrderOutputPath = Path.Combine(BaseFolder, "tailwindmodifiersorder.json");
        ModifiersPath = Path.Combine(BaseFolder, "tailwindmodifiers.json");
        TestHtmlPath = Path.Combine(BaseFolder, "Test.html");
    }

    public static string GetStem(string className)
    {
        var segments = className.Split('-').ToList();
        if (string.IsNullOrEmpty(segments[0]))
        {
            segments.RemoveAt(0);
            segments[0] = '-' + segments[0];
        }
        return segments[0];
    }

    public static string GetVariantAndSubvariantStem(string className)
    {
        var stem = GetStem(className);

        var segments = className[stem.Length..].TrimStart('-').Split('-');
        var subvariantStem = string.Join('-', segments[..(segments.Length - 1)]);

        return stem + '-' + subvariantStem;
    }

    public static string GetSubvariantValue(string className)
    {
        return className.Split('-').Last();
    }
}
