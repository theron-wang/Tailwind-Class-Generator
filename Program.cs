using AllTailwindClassesGenerator;
using System.Diagnostics;

Console.WriteLine("Tailwind Class Generator");
Console.WriteLine();

var processInfo = new ProcessStartInfo("cmd")
{
    WorkingDirectory = Helpers.BaseFolder,
    RedirectStandardOutput = true,
    RedirectStandardInput = true
};

try
{
    // Note: last Tailwind v4.0 is v4.0.17
    // Note: last Tailwind v4.1 is v4.1.18
    Console.Write("What version (enter for latest, or in format x.x.x): ");

    var version = Console.ReadLine()?.Trim();

    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(version))
    {
        Console.WriteLine("Using latest version");
        Console.WriteLine();

        version = "latest";
    }
    else if (!int.TryParse(version.Replace(".", ""), out _))
    {
        Console.WriteLine("Invalid version. Using latest");
        Console.WriteLine();

        version = "latest";
    }

    Console.Write("Minify (enter for no, or any character for yes): ");

    var minify = Console.ReadKey().Key;
    var minifyBool = true;

    Console.WriteLine();

    if (minify == ConsoleKey.Enter)
    {
        minifyBool = false;
    }

    // Minify means to not include classes like p-20; only include p-{n}
    Console.WriteLine($"Minify: {minifyBool}");
    Console.WriteLine();

    var process = new Process();
    Console.WriteLine("Updating");
    process.StartInfo = new ProcessStartInfo("cmd")
    {
        WorkingDirectory = Helpers.BaseFolder,
        RedirectStandardOutput = true,
        Arguments = $"/c npm install {(version.StartsWith('3') ? "" : $"@tailwindcss/cli@{version}")} tailwindcss@{version}"
    };

    process.Start();
    await process.WaitForExitAsync();

    Console.WriteLine($"Version: {version}");
    Console.WriteLine();

    if (version.StartsWith('3'))
    {
        await UseV3();
    }
    else
    {
        await UseV4(minifyBool);
    }
}
catch (Exception e)
{
    Console.WriteLine($"Error: " + e.Message);
    Console.WriteLine("Terminating program.");
    throw;
}

#if RELEASE
Console.ReadKey();
#endif

async Task UseV3()
{
    Console.WriteLine("Do you need to build?");
    Console.Write("(y/n): ");

    var result = Console.ReadLine()?.Trim().ToLower();

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

        await V3.PurgeFile(Path.Combine(processInfo.WorkingDirectory, "tailwind.output.css"));

        Console.WriteLine("File purge complete");
    }
    else
    {
        Console.WriteLine("Using existing file");
    }

    Console.WriteLine();
    Console.WriteLine("Extracting css classes.");
    await V3.ExtractCssClassesToOutputFile(Path.Combine(processInfo.WorkingDirectory, "tailwind.output.css"), processInfo);

    Console.WriteLine("Extraction complete.");
}

async Task UseV4(bool minify)
{
    try
    {
        if (!Directory.Exists(Helpers.V4Folder))
        {
            Directory.CreateDirectory(Helpers.V4Folder);
        }

        Console.WriteLine("Getting all classes");
        await V4.ParseAllTailwindClasses(await GetVersion());

        Console.WriteLine("Building classes");
        await V4.CompileClasses();

        Console.WriteLine("Extracting classes and descriptions");
        await V4.ExtractClassesAndDescriptions(minify);

        Console.WriteLine("Extracting default theme");
        await V4.ExtractDefaultTheme();

        Console.WriteLine("Extracting variants");
        await V4.ExtractVariants();

        Console.WriteLine("Getting class sort order");
        await V4.GetSortOrder();

        Console.WriteLine("Getting variant sort order");
        await V4.GetVariantSortOrder();

        Console.WriteLine("Extraction complete.");
    }
    finally
    {
        if (File.Exists(Path.Combine(Helpers.BaseFolder, "v4.css")))
        {
            File.Delete(Path.Combine(Helpers.BaseFolder, "v4.css"));
        }
        if (File.Exists(Path.Combine(Helpers.BaseFolder, "v4.output.css")))
        {
            File.Delete(Path.Combine(Helpers.BaseFolder, "v4.output.css"));
        }
        if (File.Exists(Path.Combine(Helpers.BaseFolder, "Test.html")))
        {
            File.Delete(Path.Combine(Helpers.BaseFolder, "Test.html"));
        }
        if (File.Exists(Path.Combine(Helpers.V4Folder, "all-classes.txt")))
        {
            File.Delete(Path.Combine(Helpers.V4Folder, "all-classes.txt"));
        }
        if (File.Exists(Path.Combine(Helpers.V4Folder, "all-variants.txt")))
        {
            File.Delete(Path.Combine(Helpers.V4Folder, "all-variants.txt"));
        }
    }
}

async Task<string> GetVersion()
{
    var processInfo = new ProcessStartInfo()
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
        FileName = "cmd",
        Arguments = "/C npm list tailwindcss --depth=0",
        WorkingDirectory = Helpers.BaseFolder
    };


    using var process = Process.Start(processInfo);
    var output = await process!.StandardOutput.ReadToEndAsync();

    await process.WaitForExitAsync();

    // Could be extra stuff in the beginning and the end
    output = output[output.IndexOf("tailwindcss")..];

    // Sample output: `-- tailwindcss@4.0.0
    var indexOfFirstDigit = -1;
    var end = 0;
    for (var i = 0; i < output.Length; i++)
    {
        if (indexOfFirstDigit == -1)
        {
            if (char.IsDigit(output[i]))
            {
                indexOfFirstDigit = i;
            }
        }
        else if (!char.IsDigit(output[i]) && output[i] != '.')
        {
            end = i;
            break;
        }
    }

    return $"v{output[indexOfFirstDigit..end].Trim()}";
}