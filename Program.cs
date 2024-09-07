using AllTailwindClassesGenerator;
using System.Diagnostics;
using System.Text.Json;

var baseFolder = Path.GetFullPath("../../../");

var outputPath = Path.Combine(baseFolder, "tailwindclasses.json");
var spacingOutputPath = Path.Combine(baseFolder, "tailwindspacing.json");
var tailwindRgbOutputPath = Path.Combine(baseFolder, "tailwindrgbmapper.json");
var opacityOutputPath = Path.Combine(baseFolder, "tailwindopacity.json");
var cssOutputPath = Path.Combine(baseFolder, "tailwinddesc.json");
var orderOutputPath = Path.Combine(baseFolder, "tailwindorder.json");
var modifiersOrderOutputPath = Path.Combine(baseFolder, "tailwindmodifiersorder.json");
var modifiersPath = Path.Combine(baseFolder, "tailwindmodifiers.json");

var testHtmlPath = Path.Combine(baseFolder, "Test.html");

Console.WriteLine("Tailwind Class Generator");
Console.WriteLine();

var processInfo = new ProcessStartInfo("cmd")
{
    WorkingDirectory = baseFolder,
    RedirectStandardOutput = true,
    RedirectStandardInput = true
};

string? result;

try
{
    Console.WriteLine("Do you need to build?");
    Console.Write("(y/n): ");

    result = Console.ReadLine()?.Trim().ToLower();

    while (string.IsNullOrWhiteSpace(result) == false && result != "y" && result != "n")
    {
        Console.Write("(y/n): ");

        result = Console.ReadLine()?.Trim().ToLower();
    }

    if (result == "y")
    {
        var process = new Process();
        Console.WriteLine("Starting build process:");
        process.StartInfo = processInfo;

        process.Start();
        await process.StandardInput.WriteLineAsync("npx tailwindcss -i ./tailwind.css -o ./tailwind.output.css && exit");
        await process.WaitForExitAsync();

        Console.WriteLine("Successfully finished building.");
        Console.WriteLine("Begin file trim");

        await PurgeFile();

        Console.WriteLine("File purge complete");
    }
    else
    {
        Console.WriteLine("Using existing file");
    }

    Console.WriteLine();
    Console.WriteLine("Extracting css classes.");
    await ExtractCssClassesToOutputFile();

    Console.WriteLine("Extraction complete.");
}
catch (Exception e)
{
    Console.WriteLine($"Error: " + e.Message);
    Console.WriteLine("Terminating program.");
    throw;
}

Console.ReadKey();

async Task PurgeFile()
{
    var cssOutput = Path.Combine(processInfo.WorkingDirectory, "tailwind.output.css");
    using var fs = File.Open(cssOutput, FileMode.Open, FileAccess.ReadWrite);

    var temp = Path.GetTempFileName();

    using var newFile = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose);

    using var reader = new StreamReader(fs);
    using (var writer = new StreamWriter(newFile, leaveOpen: true))
    {
        bool keepClass = false;

        while (reader.EndOfStream == false)
        {
            var line = await reader.ReadLineAsync();

            if (keepClass == false && string.IsNullOrWhiteSpace(line) == false && line.StartsWith('.') && line.EndsWith("::-moz-placeholder") == false)
            {
                keepClass = true;
            }

            if (keepClass)
            {
                await writer.WriteLineAsync(line?.Split("::")[0] + (line?.Contains("::") == true ? " {" : ""));
            }

            if (keepClass && line?.Contains('}') == true)
            {
                keepClass = false;
            }
        }
    }

    fs.SetLength(0);
    newFile.Position = 0;
    await newFile.CopyToAsync(fs);
}

async Task ExtractCssClassesToOutputFile()
{
    var cssOutput = Path.Combine(processInfo.WorkingDirectory, "tailwind.output.css");

    var uniqueClasses = new HashSet<string>();

    using (var fs = File.Open(cssOutput, FileMode.Open, FileAccess.Read))
    {
        using var reader = new StreamReader(fs);

        while (reader.EndOfStream == false)
        {
            var line = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(line) == false && line.StartsWith('.') && line.EndsWith('{'))
            {
                uniqueClasses.Add(line.Split(' ')[0].TrimStart('.').Replace("\\", ""));
            }
        }
    }
    uniqueClasses.Add("group");
    uniqueClasses.Add("peer");

    var classes = uniqueClasses.ToList();

    classes.Sort();
    var variants = new List<Variant>();

    foreach (var c in classes)
    {
        var stem = GetStem(c);

        if (variants.Any(v => v.Stem == stem))
        {
            continue;
        }

        var classesWithSameStem = classes.Where(cl => GetStem(cl) == stem).ToList();

        var variant = new Variant()
        {
            Stem = stem
        };

        if (classesWithSameStem.Count > 1)
        {
            foreach (var cl in classesWithSameStem)
            {
                var segments = cl[stem.Length..].TrimStart('-').Split('-');
                var subvariantStem = string.Join('-', segments[..(segments.Length - 1)]);

                var subvariantValue = segments.Last();

                switch (segments.Length)
                {
                    case 1:
                        variant.DirectVariants ??= new();
                        variant.DirectVariants.Add(subvariantValue);
                        break;
                    default:
                        if (variant.Subvariants is not null && variant.Subvariants.Any(s => s.Stem == subvariantStem))
                        {
                            continue;
                        }

                        var classesWithSameSubStem =
                            classesWithSameStem.Where(cl => GetVariantAndSubvariantStem(cl) == stem + '-' + subvariantStem).ToList();
                        if (classesWithSameSubStem.Count == 1)
                        {
                            variant.DirectVariants ??= new();
                            variant.DirectVariants.Add(subvariantStem + '-' + subvariantValue);
                        }
                        else
                        {
                            variant.Subvariants ??= new();
                            variant.Subvariants.Add(new Subvariant()
                            {
                                Stem = subvariantStem,
                                Variants = classesWithSameSubStem.Select(GetSubvariantValue).ToList()
                            });
                        }
                        break;
                }
            }
        }
        else
        {
            variant.Stem = c;
        }

        variants.Add(variant);
    }

    List<string> colors = [];
    List<int> darknesses = [];
    List<int> opacities = [];
    List<string> spacing = [];

    foreach (var v in variants)
    {
        if (v.Subvariants is not null)
        {
            if (v.Subvariants.Count == 1 && (v.DirectVariants is null || v.DirectVariants.Count == 0))
            {
                v.DirectVariants = v.Subvariants[0].Variants;
                v.Stem += '-' + v.Subvariants[0].Stem;
                v.Subvariants = null;
            }
            else
            {
                if (v.Subvariants.Any(s => s.Stem.Contains("amber")))
                {
                    if (colors.Count == 0)
                    {
                        colors = v.Subvariants.Select(s => s.Stem).ToList();

                        colors.Add("black(only)");
                        colors.Add("white(only)");
                        colors.Add("transparent(only)");
                        colors.Add("current(only)");
                        colors.Add("inherit(only)");

                        darknesses = v.Subvariants.First().Variants!.Select(v => int.Parse(v.Split('/')[0])).Distinct().ToList();
                        darknesses?.Sort();

                        opacities = v.Subvariants.First().Variants!.Where(v => v.Contains('/')).Select(v => int.Parse(v.Split('/')[1])).Distinct().ToList();
                        opacities?.Sort();
                    }

                    var colorsNormalized = colors.Select(c => c.Replace("(only)", "")).ToList();

                    if (v.Subvariants.Any(s => s.Variants!.Any(v => v.Contains('/'))) || v.DirectVariants!.Any(s => s.Contains('/')))
                    {
                        v.UseOpacity = true;
                    }
                    var removed = v.Subvariants.RemoveAll(s => colorsNormalized.Contains(s.Stem));
                    var x = v.DirectVariants!.RemoveAll(v => colorsNormalized.Any(c => v.Contains(c)));

                    if (removed > 0)
                    {
                        v.DirectVariants.Add("{c}");
                    }
                    if (v.Subvariants.Count == 0)
                    {
                        v.Subvariants = null;
                    }
                    else
                    {
                        var subvariantsToRemove = new List<Subvariant>();
                        foreach (var s in v.Subvariants)
                        {
                            var color = colorsNormalized.Where(s.Stem.Contains).FirstOrDefault();
                            var opacity = opacities!.Where(o => s.Stem.Contains(o.ToString())).FirstOrDefault();
                            var newStem = "";
                            if (string.IsNullOrWhiteSpace(color) == false)
                            {
                                newStem = s.Stem.Replace(color, "{c}");
                                if (s.Stem.Contains('/'))
                                {
                                    newStem = newStem.Replace($"/{opacity}", "");
                                    v.UseOpacity = true;
                                }
                            }
                            else
                            {
                                s.Variants?.RemoveAll(v => colorsNormalized.Any(c => v.Contains(c)));
                            }

                            if (v.Subvariants.Any(sv => sv.Stem == newStem))
                            {
                                subvariantsToRemove.Add(s);
                            }
                            else if (string.IsNullOrEmpty(newStem) == false)
                            {
                                s.Stem = newStem;
                                s.Variants = null;
                                v.UseColors = true;

                                var normalStem = newStem.Replace("-{c}", "");

                                var sub = v.Subvariants.FirstOrDefault(sv => sv.Stem == normalStem);

                                if (sub?.Variants is not null)
                                {
                                    sub.Variants.RemoveAll(colorsNormalized.Contains);

                                    if (sub.Variants.Count == 0)
                                    {
                                        sub.Variants = null;
                                    }
                                }
                            }
                        }

                        subvariantsToRemove.ForEach(s => v.Subvariants.Remove(s));

                        if (v.Subvariants.Count == 0)
                        {
                            v.Subvariants = null;
                        }
                    }

                    v.UseColors = true;
                }
                if (v.Subvariants is not null && v.Subvariants.Any(s => s.Variants is not null && s.Variants.Contains("0.5")))
                {
                    if (spacing.Count == 0)
                    {
                        var sv = v.Subvariants.First(s => s.Variants!.Contains("0.5"));
                        spacing = sv.Variants!.Select(s => s).ToList();
                    }

                    var subvariantsToRemove = new List<Subvariant>();

                    foreach (var sv in v.Subvariants.Where(s => s.Variants is not null && s.Variants.Contains("0.5")))
                    {
                        sv.Variants!.RemoveAll(spacing.Contains);
                        var newStem = sv.Stem + "-{s}";
                        v.UseSpacing = true;
                        if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                        {
                            v.DirectVariants ??= new();
                            v.DirectVariants.Add(newStem);
                        }
                        if (sv.Variants.Count == 0)
                        {
                            subvariantsToRemove.Add(sv);
                        }
                    }

                    subvariantsToRemove.ForEach(s => v.Subvariants.Remove(s));

                    if (v.Subvariants.Count == 0)
                    {
                        v.Subvariants = null;
                    }
                }
            }
        }
        if (v.DirectVariants is not null && v.DirectVariants.Any(s => s == "0.5"))
        {
            if (spacing.Count == 0)
            {
                spacing = v.DirectVariants.Where(s => s == "px" || s.All(c => char.IsDigit(c) || c == '.')).ToList();
            }

            v.DirectVariants.RemoveAll(spacing.Contains);
            if (v.DirectVariants.Count == 0)
            {
                v.DirectVariants = null;
            }
            else if (v.DirectVariants.Count > 0)
            {
                v.DirectVariants.Add("{s}");
            }

            v.UseSpacing = true;
        }

        if (v.UseColors != true && v.UseSpacing != true)
        {
            if ((v.DirectVariants is null || v.DirectVariants.Count == 0) && v.Subvariants is not null && v.Subvariants.Count == 1)
            {
                var sub = v.Subvariants[0];
                v.Stem += $"-{sub.Stem}";
                v.DirectVariants = sub.Variants;

                v.Subvariants = null;
            }
            if ((v.Subvariants is null || v.Subvariants.Count == 0) && v.DirectVariants is not null && v.DirectVariants.Count == 1)
            {
                v.Stem += $"-{v.DirectVariants[0]}";
                v.DirectVariants = null;
            }
        }

        if (v.Subvariants is not null)
        {
            var subvariantsToRemove = new List<Subvariant>();
            foreach (var subvariant in v.Subvariants)
            {
                if (subvariant.Variants is null)
                {
                    v.DirectVariants ??= new();
                    v.DirectVariants.Add(subvariant.Stem);
                    subvariantsToRemove.Add(subvariant);
                }
                else if (subvariant.Variants.Count == 1)
                {
                    v.DirectVariants ??= new();
                    v.DirectVariants.Add($"{subvariant.Stem}-{subvariant.Variants[0]}");
                    subvariantsToRemove.Add(subvariant);
                }
            }
            v.Subvariants.RemoveAll(subvariantsToRemove.Contains);
            if (v.Subvariants.Count == 0)
            {
                v.Subvariants = null;
            }
        }
    }

    var variantsToRemove = new List<Variant>();
    foreach (var negativeVariant in variants.Where(v => v.Stem.StartsWith('-')))
    {
        var nonNegativeVariant = variants.FirstOrDefault(v => v.Stem == negativeVariant.Stem.TrimStart('-'));

        if (nonNegativeVariant is null)
        {
            continue;
        }

        if (nonNegativeVariant.UseColors == negativeVariant.UseColors &&
            nonNegativeVariant.UseOpacity == negativeVariant.UseOpacity &&
            nonNegativeVariant.UseSpacing == negativeVariant.UseSpacing)
        {
            if (nonNegativeVariant.DirectVariants == negativeVariant.DirectVariants && nonNegativeVariant.DirectVariants is null)
            {
                if (nonNegativeVariant.Subvariants == negativeVariant.Subvariants && nonNegativeVariant.Subvariants is null)
                {
                    variantsToRemove.Add(negativeVariant);
                    nonNegativeVariant.HasNegative = true;
                }
                else
                {
                    var allTheSame = negativeVariant.Subvariants?.Count == nonNegativeVariant.Subvariants?.Count;

                    if (allTheSame)
                    {
                        foreach (var subvariant in nonNegativeVariant.Subvariants!)
                        {
                            var nonNegativeSubvariant = nonNegativeVariant.Subvariants.FirstOrDefault(s => s.Stem == subvariant.Stem);

                            if (nonNegativeSubvariant is null)
                            {
                                allTheSame = false;
                                break;
                            }

                            if (subvariant.Variants!.ToList().RemoveAll(v => nonNegativeSubvariant.Variants!.Contains(v)) != subvariant.Variants!.Count)
                            {
                                allTheSame = false;
                                break;
                            }
                        }
                    }

                    nonNegativeVariant.HasNegative = allTheSame;
                    variantsToRemove.Add(negativeVariant);
                }
            }
            else
            {
                if (nonNegativeVariant.DirectVariants is null || negativeVariant.DirectVariants is null)
                {
                    continue;
                }

                if (nonNegativeVariant.DirectVariants!.ToList().RemoveAll(v => negativeVariant.DirectVariants!.Contains(v)) == nonNegativeVariant.DirectVariants!.Count)
                {
                    if (nonNegativeVariant.Subvariants == negativeVariant.Subvariants && nonNegativeVariant.Subvariants is null)
                    {
                        variantsToRemove.Add(negativeVariant);
                        nonNegativeVariant.HasNegative = true;
                    }
                    else
                    {
                        var allTheSame = negativeVariant.Subvariants?.Count == nonNegativeVariant.Subvariants?.Count;

                        if (allTheSame)
                        {
                            foreach (var subvariant in nonNegativeVariant.Subvariants!)
                            {
                                var nonNegativeSubvariant = nonNegativeVariant.Subvariants.FirstOrDefault(s => s.Stem == subvariant.Stem);

                                if (nonNegativeSubvariant is null)
                                {
                                    allTheSame = false;
                                    break;
                                }

                                if (subvariant.Variants!.ToList().RemoveAll(v => nonNegativeSubvariant.Variants!.Contains(v)) != subvariant.Variants!.Count)
                                {
                                    allTheSame = false;
                                    break;
                                }
                            }
                        }

                        nonNegativeVariant.HasNegative = allTheSame;
                        variantsToRemove.Add(negativeVariant);
                    }
                }
            }
        }
    }

    variants.RemoveAll(v => variantsToRemove.Contains(v));

    var colorAndDarknessToRgb = new Dictionary<string, string>();

    var newColors = new List<string>();

    foreach (var color in colors)
    {
        if (color.EndsWith("(only)"))
        {
            newColors.Add(color.Replace("(only)", ""));
        }
        else
        {
            foreach (var darkness in darknesses!)
            {
                newColors.Add(color + "-" + darkness);
            }
        }
    }

    colors = newColors;

    using (var fs = File.Open(cssOutput, FileMode.Open, FileAccess.Read))
    {
        using var reader = new StreamReader(fs);

        var needToFindColor = false;
        string? colorKey = null;

        while (reader.EndOfStream == false)
        {
            var line = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(line) == false && line.StartsWith(".bg") && line.EndsWith('{'))
            {
                var color = (line.Split(' ')[0][".bg-".Length..]);

                if (colors.Contains(color) && colorAndDarknessToRgb.ContainsKey(color) == false)
                {
                    needToFindColor = true;
                    colorKey = color;

                    colorAndDarknessToRgb[colorKey] = "";
                }
            }
            else if (needToFindColor && colorKey is not null && string.IsNullOrEmpty(line) == false && line.StartsWith("background-color: rgb("))
            {
                var rgb = line["background-color: rgb(".Length..].Split(' ');

                var r = rgb[0];
                var g = rgb[1];
                var b = rgb[2];

                colorAndDarknessToRgb[colorKey] = $"{r},{g},{b}";
                needToFindColor = false;
                colorKey = null;
            }
        }
    }

    colorAndDarknessToRgb = colorAndDarknessToRgb
        .OrderBy(x => x.Key.Any(c => c == '-') ? int.Parse(x.Key.Split('-')[1]) : 0)
        .GroupBy(x => x.Key.Split('-')[0])
        .OrderBy(x => x.Key)
        .SelectMany(x => x.Where(y => true))
        .ToDictionary(p => p.Key, p => p.Value);

    spacing.Remove("px");
    spacing = spacing.OrderBy(double.Parse).ToList();
    spacing.Insert(0, "px");

    Console.WriteLine($"{variants.Count} variants found");
    using (var output = File.Open(outputPath!, FileMode.Create, FileAccess.Write))
    {
        await JsonSerializer.SerializeAsync(output, variants);
    }
    using (var spacingOutput = File.Open(spacingOutputPath!, FileMode.Create, FileAccess.Write))
    {
        await JsonSerializer.SerializeAsync(spacingOutput, spacing);
    }
    using (var opacityOutput = File.Open(opacityOutputPath!, FileMode.Create, FileAccess.Write))
    {
        await JsonSerializer.SerializeAsync(opacityOutput, opacities);
    }
    using var colorAndDarknessToRgbOutput = File.Open(tailwindRgbOutputPath!, FileMode.Create, FileAccess.Write);
    await JsonSerializer.SerializeAsync(colorAndDarknessToRgbOutput, colorAndDarknessToRgb);

    await GetCssDescripts(colors, spacing, variants.Where(v => v.UseSpacing == true).ToList());

    await GetSortOrder(variants, colors[0], spacing.Last());
    await GetSortOrderModifiers();
}

async Task GetCssDescripts(List<string> colors, List<string> spacings, List<Variant> variants)
{
    Console.WriteLine();
    Console.WriteLine("Writing css mapping.");

    var cssOutput = Path.Combine(processInfo.WorkingDirectory, "tailwind.output.css");
    var temp = Path.GetTempFileName();

    colors.Sort();
    var dict = new Dictionary<string, string>();
    using (var fs = File.Open(cssOutput, FileMode.Open, FileAccess.Read))
    {
        using var reader = new StreamReader(fs);

        string? activeClass = null;

        while (reader.EndOfStream == false)
        {
            var line = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(line) == false && line.StartsWith('.') && line.EndsWith('{'))
            {
                activeClass = line.Split(' ')[0].TrimStart('.').Replace("\\", "");
                if (dict.ContainsKey(activeClass) == false)
                {
                    dict.Add(activeClass, "");
                }
            }
            else if (line!.StartsWith('}'))
            {
                if (activeClass is not null)
                {
                    dict[activeClass] = dict[activeClass].Trim();
                }
                activeClass = null;
            }
            else if (activeClass is not null)
            {
                dict[activeClass] += line + " ";
            }
        }
    }

    foreach (var pair in dict.ToDictionary(p => p.Key, p => p.Value))
    {
        var segments = pair.Key.Split('/')[0].Split('-');

        var color = segments.Last();

        if (int.TryParse(color, out _))
        {
            color = $"{segments[^2]}-{color}";
        }

        if (colors.Contains(color))
        {
            if (pair.Key.Contains('/'))
            {
                var replace = pair.Key[pair.Key.IndexOf(colors.First(pair.Key.Contains))..];

                var newKey = pair.Key.Replace(replace, "{c}");

                if (dict.ContainsKey(newKey) == false)
                {
                    var indexOfRgb = pair.Value.IndexOf("rgb");
                    var length = pair.Value.IndexOf(';', indexOfRgb) - indexOfRgb;
                    var rgb = pair.Value.Substring(indexOfRgb, length);

                    dict[newKey] = pair.Value.Replace(rgb, "{0}");

                    // Sometimes both the hex and the rgb are included
                    var hexIndex = pair.Value.IndexOf('#');
                    if (hexIndex != -1)
                    {
                        var hex = pair.Value.Substring(hexIndex, 4);

                        if (hex.Skip(1).Distinct().Count() != 1)
                        {
                            hex = pair.Value.Substring(hexIndex, 7);
                        }

                        dict[newKey] = pair.Value.Replace(hex, "{1}");
                    }
                }
                dict.Remove(pair.Key);
            }
            else
            {
                var newValue = pair.Value;
                var replace = pair.Key[pair.Key.IndexOf(colors.First(pair.Key.Contains))..];

                var newKey = pair.Key.Replace(replace, "{c}");

                if (dict.ContainsKey(newKey) == false)
                {
                    if (newValue.Contains("rgb"))
                    {
                        var indexOfRgb = newValue.IndexOf("rgb");
                        var length = newValue.IndexOf('/', indexOfRgb) - indexOfRgb;
                        replace = newValue.Substring(indexOfRgb, length);

                        newValue = newValue.Replace(replace, "{0}");

                        dict[newKey] = newValue;
                    }
                    if (newValue.Contains('#'))
                    {
                        var indexOfHex = newValue.IndexOf("#");

                        try
                        {
                            replace = newValue.Substring(indexOfHex, 7);
                        }
                        catch
                        {
                            replace = newValue.Substring(indexOfHex, 4);
                        }
                        newValue = newValue.Replace(replace, "{1}");

                        dict[newKey] = newValue;
                    }
                }

                dict.Remove(pair.Key);
            }
        }
        else if (variants.Any(v => pair.Key.Split('-').Contains(v.Stem.Replace("-{s}", "")) && (
            ((v.DirectVariants is null || v.DirectVariants.Count == 0) && (v.Subvariants is null || v.Subvariants.Count == 0)) ||
            ((v.DirectVariants is not null && v.DirectVariants.Count > 0) && v.DirectVariants.Any(d => d.Contains("{s}") && pair.Key.TrimStart('-') == $"{v.Stem}-{d.Replace("{s}", pair.Key.Split('-').Last())}"))
        )))
        {
            var replace = pair.Key[(pair.Key.LastIndexOf('-') + 1)..];

            if (spacings.Contains(replace) == false)
            {
                continue;
            }

            var newKey = pair.Key.Replace(replace, "{s}");

            if (dict.ContainsKey(newKey) == false)
            {
                if (pair.Value.Contains("rem"))
                {
                    var indexOfRem = pair.Value.IndexOf("rem");
                    var indexOfSpace = pair.Value.LastIndexOf(' ', indexOfRem);
                    var length = indexOfRem + 2 - indexOfSpace;
                    replace = pair.Value.Substring(indexOfSpace + 1, length);

                    dict[newKey] = pair.Value.Replace(replace, "{0}");
                }
                else if (pair.Value.Contains("px"))
                {
                    var indexOfPx = pair.Value.IndexOf("px");
                    var indexOfSpace = pair.Value.LastIndexOf(' ', indexOfPx);
                    var length = indexOfPx + 1 - indexOfSpace;
                    replace = pair.Value.Substring(indexOfSpace + 1, length);

                    dict[newKey] = pair.Value.Replace(replace, "{0}");
                }
            }
            if (pair.Value.Contains("rem") || pair.Value.Contains("px"))
            {
                dict.Remove(pair.Key);
            }
        }
    }

    using var cssDescOutput = File.Open(cssOutputPath!, FileMode.Create, FileAccess.Write);
    await JsonSerializer.SerializeAsync(cssDescOutput, dict);
}

async Task GetSortOrderModifiers()
{
    List<string> modifiers;

    using (var file = File.Open(modifiersPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        modifiers = (await JsonSerializer.DeserializeAsync<List<string>>(file))!;
    }

    using (var file = File.Open(testHtmlPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
    {
        using var streamWriter = new StreamWriter(file);

        await streamWriter.WriteAsync(@$"<div class=""{string.Join(' ', modifiers.Select(m => $"{(m == "has" ? "has-[div]" : m)}:container"))}""></div>");
    }

    var process = new Process();
    Console.WriteLine();
    Console.WriteLine("Starting Prettier process (modifiers):");
    process.StartInfo = processInfo;

    process.Start();
    await process.StandardInput.WriteLineAsync("npm run prettier && exit");
    await process.WaitForExitAsync();

    Console.WriteLine("Prettier process complete");

    string postSortFile;

    using (var file = File.Open(testHtmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        using var streamReader = new StreamReader(file);

        postSortFile = await streamReader.ReadToEndAsync();
    }

    string classContent = postSortFile.Split('"')[1];

    var sorted = classContent.Split(' ').Select(c =>
    {
        if (c == "has-[div]")
        {
            return "has";
        }
        return c.Split(':')[0];
    });

    using (var file = File.Open(modifiersOrderOutputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
    {
        await JsonSerializer.SerializeAsync(file, sorted);
    }

    Console.WriteLine("Sort order complete");
    Console.WriteLine();
}
async Task GetSortOrder(List<Variant> variants, string firstColor, string lastSpacing)
{
    var classList = new List<string>();

    foreach (var variant in variants)
    {
        foreach (var subvariant in variant.Subvariants ?? [])
        {
            foreach (var subSubvariant in subvariant.Variants ?? [])
            {
                classList.Add($"{variant.Stem}-{subvariant.Stem}-{subSubvariant}".Replace("{c}", firstColor).Replace("{s}", lastSpacing).TrimEnd('-'));
            }
        }
        foreach (var subvariant in variant.DirectVariants ?? [])
        {
            classList.Add($"{variant.Stem}-{subvariant}".Replace("{c}", firstColor).Replace("{s}", lastSpacing).TrimEnd('-'));
        }

        if (variant.Subvariants is null && variant.DirectVariants is null)
        {
            if (variant.UseSpacing == true)
            {
                classList.Add($"{variant.Stem}-{lastSpacing}");
            }
            if (variant.UseColors == true)
            {
                classList.Add($"{variant.Stem}-{firstColor}");
            }
            if (variant.UseColors != true && variant.UseSpacing != true)
            {
                classList.Add(variant.Stem);
            }
        }
    }

    using (var file = File.Open(testHtmlPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
    {
        using var streamWriter = new StreamWriter(file);

        await streamWriter.WriteAsync(@$"<div class=""{string.Join(' ', classList)}""></div>");
    }

    var process = new Process();
    Console.WriteLine();
    Console.WriteLine("Starting Prettier process:");
    process.StartInfo = processInfo;

    process.Start();
    await process.StandardInput.WriteLineAsync("npm run prettier && exit");
    await process.WaitForExitAsync();

    Console.WriteLine("Prettier process complete");

    string postSortFile;

    using (var file = File.Open(testHtmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        using var streamReader = new StreamReader(file);

        postSortFile = await streamReader.ReadToEndAsync();
    }

    string classContent = postSortFile.Split('"')[1];

    var sorted = classContent.Split(' ').Select(c =>
    {
        if (c.EndsWith(firstColor))
        {
            return c.Replace(firstColor, "{c}");
        }
        else if (c.EndsWith(lastSpacing))
        {
            return c.Replace(lastSpacing, "{s}");
        }

        return c;
    });

    using (var file = File.Open(orderOutputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
    {
        await JsonSerializer.SerializeAsync(file, sorted);
    }

    Console.WriteLine("Sort order complete");
    Console.WriteLine();
}

string GetStem(string className)
{
    var segments = className.Split('-').ToList();
    if (string.IsNullOrEmpty(segments[0]))
    {
        segments.RemoveAt(0);
        segments[0] = '-' + segments[0];
    }
    return segments[0];
}

string GetVariantAndSubvariantStem(string className)
{
    var stem = GetStem(className);

    var segments = className[stem.Length..].TrimStart('-').Split('-');
    var subvariantStem = string.Join('-', segments[..(segments.Length - 1)]);

    return stem + '-' + subvariantStem;
}

string GetSubvariantValue(string className)
{
    return className.Split('-').Last();
}
