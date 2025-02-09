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

string? result;

try
{
    Console.WriteLine("Would you like to use Tailwind v3 or v4?");
    Console.Write("(3/4): ");

    result = Console.ReadLine()?.Trim();

    if (result == "3")
    {
        Console.WriteLine("Using Tailwind v3");

        await UseV3();
    }
    else
    {
        if (result == "4")
        {
            Console.WriteLine("Using Tailwind v4");
        }
        else
        {
            Console.WriteLine("Invalid input. Defaulting to Tailwind v4.");
        }

        await UseV4();
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

async Task UseV4()
{
    try
    {
        if (!Directory.Exists(Helpers.V4Folder))
        {
            Directory.CreateDirectory(Helpers.V4Folder);
        }

        Console.WriteLine("Getting all classes");
        await V4.GenerateClassesFromV3();

        Console.WriteLine("Building classes");
        await V4.CompileClasses();

        Console.WriteLine("Extracting classes and descriptions");
        await V4.ExtractClassesAndDescriptions();

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