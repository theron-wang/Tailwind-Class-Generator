using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllTailwindClassesGenerator;
internal class V4
{
    public static async Task GenerateClassesFromV3()
    {
        var baseClasses = Path.Combine(Helpers.BaseFolder, "tailwindclasses-base.json");

        List<V3.Variant>? variants;

        using (var fs = new FileStream(baseClasses, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            variants = await JsonSerializer.DeserializeAsync<List<V3.Variant>>(fs);
        }

        Debug.Assert(variants != null);

        var classes = new List<string>();

        foreach (var variant in variants)
        {
            var variantClasses = new List<string>();

            if (variant.DirectVariants != null && variant.DirectVariants.Count > 0)
            {
                foreach (var v in variant.DirectVariants)
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        variantClasses.Add(variant.Stem);
                    }
                    else
                    {
                        if (v.Contains("{s}"))
                        {
                            variantClasses.Add(variant.Stem + "-" + v.Replace("{s}", "px"));
                        }
                        else if (v.Contains("{c}"))
                        {
                            variantClasses.Add(variant.Stem + "-" + v.Replace("{c}", "black"));
                        }
                        else
                        {
                            variantClasses.Add(variant.Stem + "-" + v);
                        }
                    }
                }
            }

            if (variant.Subvariants != null && variant.Subvariants.Count > 0)
            {
                // Do the same check for each of the subvariants as above

                foreach (var subvariant in variant.Subvariants)
                {
                    if (subvariant.Variants != null)
                    {
                        foreach (var v in subvariant.Variants)
                        {
                            if (string.IsNullOrWhiteSpace(v))
                            {
                                variantClasses.Add(variant.Stem + "-" + subvariant.Stem);
                            }
                            else
                            {
                                variantClasses.Add(variant.Stem + "-" + subvariant.Stem + "-" + v);
                            }
                        }
                    }

                    if (subvariant.Stem.Contains("{c}"))
                    {
                        variantClasses.Add(variant.Stem + "-" + subvariant.Stem.Replace("{c}", "black"));
                    }
                    else if (subvariant.Stem.Contains("{s}"))
                    {
                        variantClasses.Add(variant.Stem + "-" + subvariant.Stem.Replace("{s}", "px"));
                    }
                }
            }

            if ((variant.DirectVariants == null || variant.DirectVariants.Count == 0) && (variant.Subvariants == null || variant.Subvariants.Count == 0))
            {
                var name = variant.Stem;
                if (variant.UseColors == true)
                {
                    name += "-black";
                }
                else if (variant.UseSpacing == true)
                {
                    name += "-px";
                }
                variantClasses.Add(name);
            }

            classes.AddRange(variantClasses);

            if (variant.HasNegative == true)
            {
                classes.AddRange(variantClasses.Select(c => $"-{c}"));
            }
        }

        var addedClassesPath = Path.Combine(Helpers.BaseFolder, "v4-added-classes.txt");

        {
            using var fs = new FileStream(addedClassesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sr = new StreamReader(fs);
            while (await sr.ReadLineAsync() is var line && line is not null)
            {
                line = line.Trim();
                classes.Add(line);
                // 33, 2/7, 51% are not included anywhere so we know they are added synthetically here
                classes.Add($"-{line}");
                classes.Add($"{line}-px");
                classes.Add($"-{line}-px");
                classes.Add($"{line}-33");
                classes.Add($"-{line}-33");
                classes.Add($"{line}-black");
                classes.Add($"{line}-51%");
                classes.Add($"-{line}-51%");
                classes.Add($"{line}-2/7");
                classes.Add($"-{line}-2/7");
            }
        }

        await WriteAllClasses(classes);
    }

    public static async Task CompileClasses()
    {
        using (var fs = new FileStream(Path.Combine(Helpers.BaseFolder, "v4.css"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            using var sw = new StreamWriter(fs);
            await sw.WriteLineAsync("@import \"tailwindcss\" source(none);");
            await sw.WriteLineAsync("@source \"./v4/all-classes.txt\";");
        }

        var processInfo = new ProcessStartInfo("cmd")
        {
            WorkingDirectory = Helpers.BaseFolder,
            Arguments = "/C npx @tailwindcss/cli -i ./v4.css -o ./v4.output.css"
        };

        using var process = Process.Start(processInfo);
        await process!.WaitForExitAsync();
    }

    public static async Task ExtractClassesAndDescriptions()
    {
        var outputPath = Path.Combine(Helpers.BaseFolder, "v4.output.css");
        HashSet<string> uniqueClasses = await GetClassesFromCssFile(outputPath);

        uniqueClasses.Add("group");
        uniqueClasses.Add("peer");

        var classes = uniqueClasses.ToList();

        classes.Sort();

        var variants = new List<Variant>();

        foreach (var c in classes)
        {
            var stem = Helpers.GetStem(c);

            if (variants.Any(v => v.Stem == stem))
            {
                continue;
            }

            var classesWithSameStem = classes.Where(cl => Helpers.GetStem(cl) == stem).ToList();

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
                                classesWithSameStem.Where(cl => Helpers.GetVariantAndSubvariantStem(cl) == stem + '-' + subvariantStem).ToList();
                            
                            variant.Subvariants ??= [];
                            variant.Subvariants.Add(new Subvariant()
                            {
                                Stem = subvariantStem,
                                Variants = classesWithSameSubStem.Select(Helpers.GetSubvariantValue).ToList()
                            });
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

        // Group and compress

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
                    if (v.Subvariants is not null)
                    {
                        var subvariantsToRemove = new List<Subvariant>();

                        foreach (var sv in v.Subvariants)
                        {
                            if (sv.Variants is null)
                            {
                                continue;
                            }

                            if (sv.Variants.Remove("black"))
                            {
                                var newStem = sv.Stem + "-{c}";
                                v.UseColors = true;
                                if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                                {
                                    v.DirectVariants ??= [];
                                    v.DirectVariants.Add(newStem);
                                }
                            }

                            if (sv.Variants.Remove("px"))
                            {
                                var newStem = sv.Stem + "-{s}";
                                v.UseSpacing = true;
                                v.UseNumbers = null;
                                if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                                {
                                    v.DirectVariants ??= [];
                                    v.DirectVariants.Add(newStem);
                                }
                            }

                            if (sv.Variants.Any(v => v.EndsWith('%')))
                            {
                                sv.Variants.Remove("51%");
                                var newStem = sv.Stem + "-{%}";
                                v.UsePercent = true;
                                if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                                {
                                    v.DirectVariants ??= [];
                                    v.DirectVariants.Add(newStem);
                                }
                            }

                            if (sv.Variants.Any(v => v.Contains('/')))
                            {
                                sv.Variants.Remove("2/7");
                                var newStem = sv.Stem + "-{f}";
                                v.UseFractions = true;
                                if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                                {
                                    v.DirectVariants ??= [];
                                    v.DirectVariants.Add(newStem);
                                }
                            }

                            if (sv.Variants.Any(v => int.TryParse(v, out _)))
                            {
                                sv.Variants.Remove("33");
                                if (v.UseSpacing != true)
                                {
                                    var newStem = sv.Stem + "-{n}";
                                    v.UseSpacing = null;
                                    v.UseNumbers = true;
                                    if (v.DirectVariants is null || v.DirectVariants.Contains(newStem) == false)
                                    {
                                        v.DirectVariants ??= [];
                                        v.DirectVariants.Add(newStem);
                                    }
                                }
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

            if (v.DirectVariants is not null)
            {
                if (v.DirectVariants.Remove("px"))
                {
                    v.DirectVariants.Add("{s}");
                    v.UseSpacing = true;
                    v.UseNumbers = null;
                }

                if (v.DirectVariants.Remove("black"))
                {
                    v.DirectVariants.Add("{c}");
                    v.UseColors = true;
                }

                if (v.DirectVariants.Any(v => v.EndsWith('%')))
                {
                    v.DirectVariants.Remove("51%");
                    v.DirectVariants.Add("{%}");
                    v.UsePercent = true;
                }

                if (v.DirectVariants.Any(v => v.Contains('/')))
                {
                    v.DirectVariants.Remove("2/7");
                    v.DirectVariants.Add("{f}");
                    v.UseFractions = true;
                }

                if (v.DirectVariants.Any(v => int.TryParse(v, out _)))
                {
                    v.DirectVariants.Remove("33");
                    if (v.UseSpacing != true)
                    {
                        v.DirectVariants.Add("{n}");
                        v.UseNumbers = true;
                    }
                }

                if (v.DirectVariants.Count == 0)
                {
                    v.DirectVariants = null;
                }
            }

            if (v.UseColors != true && v.UseSpacing != true && v.UseFractions != true && v.UsePercent != true)
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
                        v.DirectVariants ??= [];
                        v.DirectVariants.Add(subvariant.Stem);
                        subvariantsToRemove.Add(subvariant);
                    }
                    else if (subvariant.Variants.Count == 1)
                    {
                        v.DirectVariants ??= [];
                        v.DirectVariants.Add($"{subvariant.Stem}-{subvariant.Variants[0]}");
                        subvariantsToRemove.Add(subvariant);
                    }
                    else
                    {
                        // Sort numerically first, then alphabetically
                        // i.e. prevent 5 from appearing after 40 and before 50
                        subvariant.Variants = [.. subvariant.Variants.OrderBy(item =>
                        {
                            bool isNumeric = double.TryParse(item.TrimEnd('%'), out double numericValue);
                            return isNumeric ? 0 : 1;
                        })
                        .ThenBy(item =>
                        {
                            if (double.TryParse(item.TrimEnd('%'), out double numericValue))
                                return numericValue;
                            else
                                return int.MaxValue;
                        })
                        .ThenBy(item => item)];
                    }
                }
                v.Subvariants.RemoveAll(subvariantsToRemove.Contains);
                if (v.Subvariants.Count == 0)
                {
                    v.Subvariants = null;
                }
            }

            if (v.DirectVariants is not null)
            {
                v.DirectVariants = [.. v.DirectVariants.OrderBy(item =>
                {
                    bool isNumeric = double.TryParse(item.TrimEnd('%'), out double numericValue);
                    return isNumeric ? 0 : 1;
                })
                .ThenBy(item =>
                {
                    if (double.TryParse(item.TrimEnd('%'), out double numericValue))
                        return numericValue;
                    else
                        return int.MaxValue;
                })
                .ThenBy(item => item)];
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
                nonNegativeVariant.UseSpacing == negativeVariant.UseSpacing &&
                nonNegativeVariant.UsePercent == negativeVariant.UsePercent &&
                nonNegativeVariant.UseFractions == negativeVariant.UseFractions &&
                nonNegativeVariant.UseNumbers == negativeVariant.UseNumbers)
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

        variants.RemoveAll(variantsToRemove.Contains);

        // Check to see which ones support arbitrary values

        var arbitraryToVariants = new Dictionary<string, Variant>();
        var arbitraryToSubvariants = new Dictionary<string, Subvariant>();

        foreach (var variant in variants)
        {
            if (variant.UseColors == true || variant.UseSpacing == true || variant.UseFractions == true || variant.UsePercent == true || variant.UseNumbers == true)
            {
                continue;
            }

            arbitraryToVariants[$"{variant.Stem}-(--my-var)"] = variant;

            if (variant.Subvariants is null)
            {
                continue;
            }

            foreach (var subvariant in variant.Subvariants)
            {
                arbitraryToSubvariants[$"{variant.Stem}-{subvariant.Stem}-(--my-var)"] = subvariant;
            }
        }

        var classesOutputPath = Path.Combine(Helpers.V4Folder, "all-classes.txt");

        var classesToWrite = arbitraryToVariants.Keys.Concat(arbitraryToSubvariants.Keys);

        using (var fs = new FileStream(classesOutputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            using var sw = new StreamWriter(fs);
            foreach (var c in classesToWrite)
            {
                await sw.WriteLineAsync(c);
            }
        }

        await CompileClasses();

        uniqueClasses = await GetClassesFromCssFile(outputPath);

        foreach (var clazz in uniqueClasses)
        {
            if (arbitraryToVariants.TryGetValue(clazz, out Variant? value))
            {
                value.HasArbitrary = true;
            }
            else if (arbitraryToSubvariants.TryGetValue(clazz, out Subvariant? val))
            {
                val.HasArbitrary = true;
            }
        }

        using (var final = File.Open(Path.Combine(Helpers.V4Folder, "classes.json"), FileMode.Create, FileAccess.Write))
        {
            await JsonSerializer.SerializeAsync(final, variants);
        }

        await ExtractDescriptions(variants);
    }

    public static async Task ExtractDefaultTheme()
    {
        // Start at :root, :host {
        // end at next }

        var outputPath = Path.Combine(Helpers.BaseFolder, "v4.output.css");

        Dictionary<string, string> theme = [];
        Dictionary<string, string> colors = [];

        var started = false;

        var sb = new StringBuilder();

        using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var sr = new StreamReader(fs);

            while (sr.EndOfStream == false)
            {
                var line = (await sr.ReadLineAsync())?.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (!started)
                {
                    if (line.Contains(":root, :host"))
                    {
                        started = true;
                    }
                    continue;
                }

                if (line.Contains('}'))
                {
                    break;
                }

                sb.AppendLine(line);
            }
        }

        var lines = sb.ToString().Split(';');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var removeExtraSpace = string.Join(' ', line.Replace(Environment.NewLine, "").Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));

            var parts = removeExtraSpace.Split(':');
            if (parts.Length != 2)
            {
                continue;
            }
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.StartsWith("--color"))
            {
                colors[key["--color-".Length..]] = value;
            }
            else
            {
                theme[key] = value;
            }
        }

        using (var colorsOutput = File.Open(Path.Combine(Helpers.V4Folder, "colors.json"), FileMode.Create, FileAccess.Write))
        {
            await JsonSerializer.SerializeAsync(colorsOutput, colors);
        }

        using var themeOutput = File.Open(Path.Combine(Helpers.V4Folder, "theme.json"), FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(themeOutput, theme);
    }

    public static async Task ExtractVariants()
    {
        var variantsPath = Path.Combine(Helpers.BaseFolder, "v4-variants.txt");

        Dictionary<string, string> variantToDescription = [];

        using var fs = new FileStream(variantsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs);

        while (sr.EndOfStream == false)
        {
            var line = (await sr.ReadLineAsync())?.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('\t');

            variantToDescription[parts[0].Trim()] = parts[1].Trim();
        }

        using var variantsOutput = File.Open(Path.Combine(Helpers.V4Folder, "variants.json"), FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(variantsOutput, variantToDescription);
    }

    public static async Task GetSortOrder()
    {
        List<Variant>? classes;

        var classPath = Path.Combine(Helpers.V4Folder, "classes.json");

        using (var fs = new FileStream(classPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            classes = await JsonSerializer.DeserializeAsync<List<Variant>>(fs);
        }

        Debug.Assert(classes != null);

        var generatedToActual = GetGeneratedToActual(classes);

        var content = $"<div class=\"{string.Join(' ', generatedToActual.Keys)}\"></div>";

        var testHtmlPath = Path.Combine(Helpers.BaseFolder, "Test.html");

        using (var fs = new FileStream(testHtmlPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            using var sw = new StreamWriter(fs);
            await sw.WriteLineAsync(content);
        }

        var processInfo = new ProcessStartInfo("cmd")
        {
            WorkingDirectory = Helpers.BaseFolder,
            Arguments = "/C npm run prettier"
        };

        using (var process = Process.Start(processInfo))
        {
            await process!.WaitForExitAsync();
        }

        using (var fs = new FileStream(testHtmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using var sr = new StreamReader(fs);
            content = await sr.ReadToEndAsync();
        }

        var start = content!.IndexOf('"') + 1;
        var end = content.LastIndexOf('"');
        var classesInOrder = content[start..end].Split(' ').Select(c => c.Trim()).ToList();

        var actualClassesInOrder = classesInOrder.Select(c => generatedToActual[c]).ToList();

        using var final = new FileStream(Path.Combine(Helpers.V4Folder, "order.json"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await JsonSerializer.SerializeAsync(final, actualClassesInOrder);
    }

    public static async Task GetVariantSortOrder()
    {
        Dictionary<string, string>? variants;

        var classPath = Path.Combine(Helpers.V4Folder, "variants.json");

        using (var fs = new FileStream(classPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            variants = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fs);
        }

        Debug.Assert(variants != null);

        var generatedToActual = GetGeneratedToActual(variants.Keys.ToList());

        var content = $"<div class=\"{string.Join(' ', generatedToActual.Keys.Select(k => $"{k}:p-0"))}\"></div>";

        var testHtmlPath = Path.Combine(Helpers.BaseFolder, "Test.html");

        using (var fs = new FileStream(testHtmlPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            using var sw = new StreamWriter(fs);
            await sw.WriteLineAsync(content);
        }

        var processInfo = new ProcessStartInfo("cmd")
        {
            WorkingDirectory = Helpers.BaseFolder,
            Arguments = "/C npm run prettier"
        };

        using (var process = Process.Start(processInfo))
        {
            await process!.WaitForExitAsync();
        }

        using (var fs = new FileStream(testHtmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using var sr = new StreamReader(fs);
            content = await sr.ReadToEndAsync();
        }

        var start = content!.IndexOf('"') + 1;
        var end = content.LastIndexOf('"');
        var variantsInOrder = content[start..end].Split(' ').Select(c => c.Trim().Split(':')[0]).ToList();

        var actualVariantsInOrder = variantsInOrder.Select(c => generatedToActual[c]).ToList();

        using var final = new FileStream(Path.Combine(Helpers.V4Folder, "variantorder.json"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await JsonSerializer.SerializeAsync(final, actualVariantsInOrder);
    }

    private static async Task ExtractDescriptions(List<Variant> variants)
    {
        var generatedToActual = GetGeneratedToActual(variants);

        await WriteAllClasses([.. generatedToActual.Keys]);
        await CompileClasses();

        var outputPath = Path.Combine(Helpers.BaseFolder, "v4.output.css");

        var dict = new Dictionary<string, string>();
        using (var fs = File.Open(outputPath, FileMode.Open, FileAccess.Read))
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

        Debug.Assert(dict.Keys.All(generatedToActual.ContainsKey) && dict.Keys.Count == generatedToActual.Keys.Count);

        var descriptions = new Dictionary<string, string>();

        foreach (var (name, description) in dict)
        {
            string processedDescription = description;

            var actualName = generatedToActual[name];
            if (actualName.Contains("{s}"))
            {
                processedDescription = processedDescription.Replace("1px", "{0}");
            }
            else if (actualName.Contains("{c}"))
            {
                processedDescription = processedDescription.Replace("var(--color-black)", "{0}");
            }
            else if (actualName.Contains("{%}"))
            {
                processedDescription = processedDescription.Replace("51%", "{0}");
            }
            else if (actualName.Contains("{n}"))
            {
                processedDescription = processedDescription.Replace("33", "{0}");
            }
            else if (actualName.Contains("{f}"))
            {
                processedDescription = processedDescription.Replace("2/7", "{0}");
            }
            else if (actualName.Contains("{a}"))
            {
                processedDescription = processedDescription.Replace("var(--my-var)", "{0}");
            }

            descriptions[actualName] = processedDescription;
        }

        using var cssDescOutput = File.Open(Path.Combine(Helpers.V4Folder, "descriptions.json"), FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(cssDescOutput, descriptions);
    }

    private static async Task WriteAllClasses(List<string> classes)
    {
        var outputPath = Path.Combine(Helpers.V4Folder, "all-classes.txt");

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var sw = new StreamWriter(fs);
        foreach (var c in classes)
        {
            await sw.WriteLineAsync(c);
        }
    }

    private static async Task<HashSet<string>> GetClassesFromCssFile(string outputPath)
    {
        HashSet<string> uniqueClasses = [];

        var started = false;

        using var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs);

        while (sr.EndOfStream == false)
        {
            var line = (await sr.ReadLineAsync())?.Trim();

            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (!started)
            {
                if (line.Contains("@layer utilities"))
                {
                    started = true;
                }
                continue;
            }

            if (string.IsNullOrEmpty(line) == false && line.StartsWith('.') && line.EndsWith('{'))
            {
                uniqueClasses.Add(line.Split(' ')[0].TrimStart('.').Replace("\\", ""));
            }

            if (line.Contains("@keyframes"))
            {
                break;
            }
        }

        return uniqueClasses;
    }

    /// <summary>
    /// i.e. get bg-black --> bg-{c}, p-px --> p-{s}
    /// </summary>
    private static Dictionary<string, string> GetGeneratedToActual(List<Variant> classes)
    {
        Dictionary<string, string> generatedToActual = [];

        foreach (var @class in classes)
        {
            if (@class.DirectVariants is not null)
            {
                foreach (var v in @class.DirectVariants)
                {
                    if (@class.UseFractions == true)
                    {
                        var components = v.Split('/');
                        if (components.Length == 2 && components.All(c => int.TryParse(c, out _)))
                        {
                            continue;
                        }
                    }
                    if (@class.UsePercent == true && v.EndsWith('%'))
                    {
                        continue;
                    }
                    if (@class.UseNumbers == true && int.TryParse(v, out _))
                    {
                        continue;
                    }

                    var generate = v.Replace("{s}", "px").Replace("{c}", "black").Replace("{n}", "33").Replace("{f}", "2/7").Replace("{%}", "51%");

                    if (string.IsNullOrWhiteSpace(v))
                    {
                        generatedToActual[@class.Stem] = @class.Stem;
                    }
                    else
                    {
                        generatedToActual[$"{@class.Stem}-{generate}"] = $"{@class.Stem}-{v}";
                    }
                }
            }

            if (@class.Subvariants is not null)
            {
                foreach (var sv in @class.Subvariants)
                {
                    foreach (var v in sv.Variants ?? [])
                    {
                        if (@class.UseFractions == true)
                        {
                            var components = v.Split('/');
                            if (components.Length == 2 && components.All(c => int.TryParse(c, out _)))
                            {
                                continue;
                            }
                        }
                        if (@class.UsePercent == true && v.EndsWith('%'))
                        {
                            continue;
                        }
                        if (@class.UseNumbers == true && int.TryParse(v, out _))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(v))
                        {
                            generatedToActual[$"{@class.Stem}-{sv.Stem}"] = $"{@class.Stem}-{sv.Stem}";
                        }
                        else
                        {
                            generatedToActual[$"{@class.Stem}-{sv.Stem}-{v}"] = $"{@class.Stem}-{sv.Stem}-{v}";
                        }
                    }

                    if (sv.HasArbitrary == true)
                    {
                        generatedToActual[$"{@class.Stem}-{sv.Stem}-(--my-var)"] = $"{@class.Stem}-{sv.Stem}-{{a}}";
                    }
                }
            }

            if (@class.HasArbitrary == true)
            {
                generatedToActual[$"{@class.Stem}-(--my-var)"] = $"{@class.Stem}-{{a}}";
            }
        }

        return generatedToActual;
    }
    /// <summary>
    /// i.e. get bg-black --> bg-{c}, p-px --> p-{s}
    /// </summary>
    private static Dictionary<string, string> GetGeneratedToActual(List<string> variants)
    {
        Dictionary<string, string> generatedToActual = [];

        foreach (var variant in variants)
        {
            if (variant.EndsWith("..."))
            {
                generatedToActual[variant.Replace("...", "div")] = variant;
            }
            else
            {
                generatedToActual[variant] = variant;
            }
        }

        return generatedToActual;
    }

    private class Variant
    {
        [JsonPropertyName("s")]
        public required string Stem { get; set; }

        [JsonPropertyName("sv")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Subvariant>? Subvariants { get; set; }

        [JsonPropertyName("dv")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? DirectVariants { get; set; }

        [JsonPropertyName("c")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseColors { get; set; }

        [JsonPropertyName("sp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseSpacing { get; set; }

        [JsonPropertyName("p")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UsePercent { get; set; }

        [JsonPropertyName("f")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseFractions { get; set; }

        [JsonPropertyName("d")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseNumbers { get; set; }

        [JsonPropertyName("n")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HasNegative { get; set; }

        /// <summary>
        /// The absence of this property does not necessarily mean that arbitrary values are not supported.
        /// Colors, spacing, percentages, fractions, numbers all support this already, but theirs will be 
        /// set to null. This property is only for the ones that don't fall into those categories.
        /// </summary>
        [JsonPropertyName("a")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HasArbitrary { get; set; }
    }

    private class Subvariant
    {
        [JsonPropertyName("ss")]
        public required string Stem { get; set; }
        [JsonPropertyName("v")]

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Variants { get; set; }

        /// <summary>
        /// The absence of this property does not necessarily mean that arbitrary values are not supported.
        /// Colors, spacing, percentages, fractions, numbers all support this already, but theirs will be 
        /// set to null. This property is only for the ones that don't fall into those categories.
        /// </summary>
        [JsonPropertyName("a")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HasArbitrary { get; set; }
    }
}
