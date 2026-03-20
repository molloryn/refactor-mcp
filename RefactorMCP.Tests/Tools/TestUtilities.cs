using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RefactorMCP.Tests;

public static class TestUtilities
{
    private static readonly string[] SolutionFileNames = ["RefactorMCP.slnx", "RefactorMCP.sln"];
    private static readonly string[] ExampleCodeRelativePaths =
    [
        Path.Combine("RefactorMCP.Tests", "Tools", "ExampleCode.cs"),
        Path.Combine("RefactorMCP.Tests", "ExampleCode.cs")
    ];

    public static string GetSolutionPath()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            foreach (var solutionFileName in SolutionFileNames)
            {
                var solutionFile = Path.Combine(dir.FullName, solutionFileName);
                if (File.Exists(solutionFile))
                    return solutionFile;
            }
            dir = dir.Parent;
        }
        return "./RefactorMCP.slnx";
    }

    public static async Task CreateTestFile(string filePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, content);
    }

    public static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    public static string GetExampleCodePath()
    {
        var root = Path.GetDirectoryName(GetSolutionPath())!;
        foreach (var relativePath in ExampleCodeRelativePaths)
        {
            var fullPath = Path.Combine(root, relativePath);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return Path.Combine(root, ExampleCodeRelativePaths[0]);
    }

    public static string GetTestAssetPath(params string[] relativeSegments)
    {
        var root = Path.GetDirectoryName(GetSolutionPath())!;
        var path = Path.Combine(root, "RefactorMCP.Tests", "TestAssets");
        foreach (var segment in relativeSegments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    public static async Task<string> PrepareIsolatedFixtureAsync(string fixtureRelativePath, string destinationRoot)
    {
        var fixtureSource = GetTestAssetPath(fixtureRelativePath);
        var fixtureName = Path.GetFileName(fixtureSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var fixtureDestination = Path.Combine(destinationRoot, fixtureName);

        CopyDirectory(fixtureSource, fixtureDestination);

        var solutionPath = Directory.GetFiles(fixtureDestination, "*.slnx", SearchOption.TopDirectoryOnly)[0];
        await RunDotNetBuildAsync(solutionPath);
        return solutionPath;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static async Task RunDotNetBuildAsync(string solutionPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(solutionPath)!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add(solutionPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet build failed for '{solutionPath}'.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    public static string GetSampleCodeForExtractMethod() => """
using System;
public class TestClass
{
    public int Calculate(int a, int b)
    {
        if (a < 0 || b < 0)
        {
            throw new ArgumentException(\"Negative numbers not allowed\");
        }

        var result = a + b;
        return result;
    }
}
""";

    public static string GetSampleCodeForIntroduceField() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForIntroduceVariable() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForMakeFieldReadonly() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForMakeFieldReadonlyNoInit() => """
using System;
public class TestClass
{
    private string description;
}
""";

    public static string GetSampleCodeForTransformSetter() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForConvertToStaticInstance() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForMoveStaticMethod() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForMoveStaticMethodWithUsings() => """
using System;
using System.Collections.Generic;

public class TestClass
{
    public static void PrintList(List<int> numbers)
    {
        Console.WriteLine(string.Join(",", numbers));
    }
}

public class UtilClass { }
""";

    public static string GetSampleCodeForMoveInstanceMethod() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForConvertToExtension() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForSafeDelete() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForMoveInstanceMethodWithDependencies() => """
using System;
using System.Collections.Generic;

namespace Test.Domain
{
    public class OrderProcessor
    {
        private readonly string processorId;
        private List<string> log = new();

        public OrderProcessor(string id)
        {
            processorId = id;
        }

        public bool ValidateOrder(decimal amount)
        {
            return amount > 0;
        }

        // This method should be moved to PaymentService
        private bool ProcessPayment(decimal amount, string cardNumber)
        {
            if (string.IsNullOrEmpty(cardNumber))
                return false;

            log.Add($"Processing payment of {amount} for processor {processorId}");

            // Simulate payment processing
            return amount <= 1000;
        }

        public void CompleteOrder(decimal amount, string cardNumber)
        {
            if (ValidateOrder(amount) && ProcessPayment(amount, cardNumber))
            {
                log.Add("Order completed successfully");
            }
        }
    }

    public class PaymentService
    {
        // Target class for the moved method
    }
}
""";
    public static string GetSampleCodeForInlineMethod() => """
using System;

public class InlineSample
{
    private void Helper()
    {
        Console.WriteLine("Hi");
    }

    public void Call()
    {
        Helper();
        Console.WriteLine("Done");
    }
}
""";

    public static string GetSampleCodeForCleanupUsings() => """
using System;
using System.Text;

public class CleanupSample
{
    public void Say() => Console.WriteLine("Hi");
}
""";

    public static string GetSampleCodeForMoveTypeToFile() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForRenameSymbol() =>
        File.ReadAllText(GetExampleCodePath());

    public static string GetSampleCodeForExtractInterface() => """
public class Person
{
    public string Name { get; set; }
    public void Greet() { }
}
""";

    public static string GetSampleCodeForFeatureFlag() => """
using System;

public class FeatureService
{
    private readonly IFeatureFlags featureFlags;

    public FeatureService(IFeatureFlags featureFlags)
    {
        this.featureFlags = featureFlags;
    }

    public void DoWork()
    {
        if (featureFlags.IsEnabled("CoolFeature"))
        {
            Console.WriteLine("New path");
        }
        else
        {
            Console.WriteLine("Old path");
        }
    }
}

public interface IFeatureFlags
{
    bool IsEnabled(string name);
}
""";

    public static string GetSampleCodeForDecorator() => """
public class Greeter
{
    public void Greet(string name)
    {
        Console.WriteLine("Hello {name}");
    }
}
""";

    public static string GetSampleCodeForAdapter() => """
public class LegacyLogger
{
    public void Write(string message)
    {
        Console.WriteLine(message);
    }
}
""";

    public static string GetSampleCodeForObserver() => """
public class Counter
{
    private int _value;
    public void Update(int value)
    {
        _value = value;
    }
}
""";

    public static string GetSampleCodeForUseInterface() => """
public interface IWriter { void Write(string value); }
public class FileWriter : IWriter { public void Write(string value) { } }
public class C
{
    public void DoWork(FileWriter writer)
    {
        writer.Write("hi");
    }
}
""";
}

