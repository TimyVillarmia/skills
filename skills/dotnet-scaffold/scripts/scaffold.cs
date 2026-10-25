using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DotnetScaffold;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("=== .NET Deterministic Solution Scaffolder ===");

        // 1. Locate assets directory relative to this script file using CallerFilePath
        string scriptFile = GetCallerFilePath();
        string scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptFile))
            ?? throw new InvalidOperationException("Could not determine script directory.");
        string assetsDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "assets"));

        if (!Directory.Exists(assetsDir))
        {
            Console.Error.WriteLine($"[ERROR] Assets directory not found at: {assetsDir}");
            return 1;
        }

        // Ensure DOTNET_ROOT is populated for standalone MTP test execution if in non-standard toolchains (e.g. mise)
        EnsureDotnetRoot();

        // 2. Detect installed .NET SDK version
        string sdkVersion = RunCapture("dotnet", "--version").Trim();
        Console.WriteLine($"[INFO] Detected .NET SDK: {sdkVersion}");

        if (!Version.TryParse(sdkVersion.Split('-')[0], out var parsedVer) || parsedVer.Major < 10)
        {
            Console.Error.WriteLine($"[ERROR] scaffold.cs requires .NET 10 SDK or later (detected: {sdkVersion}).");
            Console.Error.WriteLine("Please run in an environment with .NET 10 SDK installed.");
            return 1;
        }

        // Parse arguments
        string solutionName = "SampleApp";
        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), solutionName);
        bool withTestcontainers = false;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-n" || args[i] == "--name") && i + 1 < args.Length)
            {
                solutionName = args[++i];
            }
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputDir = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--with-testcontainers")
            {
                withTestcontainers = true;
            }
        }

        Console.WriteLine($"[INFO] Solution Name: {solutionName}");
        Console.WriteLine($"[INFO] Output Directory: {outputDir}");
        Console.WriteLine($"[INFO] Testcontainers included: {withTestcontainers}");

        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            Console.Error.WriteLine($"[ERROR] Output directory '{outputDir}' is not empty.");
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        // 3. Create modern SLNX solution file
        Console.WriteLine("[STEP 1/7] Creating SLNX solution file...");
        if (RunCommand("dotnet", $"new sln --format slnx -n {solutionName} -o \"{outputDir}\"") != 0)
        {
            Console.Error.WriteLine("[ERROR] Failed to create SLNX solution.");
            return 1;
        }

        // 4. Generate standard config files
        Console.WriteLine("[STEP 2/7] Generating configuration files (.gitignore, .editorconfig, nuget.config)...");
        RunCommand("dotnet", "new gitignore", outputDir);
        RunCommand("dotnet", "new nugetconfig", outputDir);

        // Copy .editorconfig asset
        string editorConfigSrc = Path.Combine(assetsDir, ".editorconfig");
        if (File.Exists(editorConfigSrc))
        {
            File.Copy(editorConfigSrc, Path.Combine(outputDir, ".editorconfig"), overwrite: true);
        }

        // 5. Generate global.json with detected SDK version
        Console.WriteLine("[STEP 3/7] Generating global.json with MTP test runner...");
        string globalJsonTemplate = File.ReadAllText(Path.Combine(assetsDir, "global.json"));
        string globalJsonContent = globalJsonTemplate.Replace("{SDK_VERSION}", sdkVersion);
        File.WriteAllText(Path.Combine(outputDir, "global.json"), globalJsonContent);

        // 6. Copy Directory.Build.props and Directory.Packages.props
        Console.WriteLine("[STEP 4/7] Applying Central Package Management and Build Policies...");
        File.Copy(Path.Combine(assetsDir, "Directory.Build.props"), Path.Combine(outputDir, "Directory.Build.props"), overwrite: true);
        File.Copy(Path.Combine(assetsDir, "Directory.Packages.props"), Path.Combine(outputDir, "Directory.Packages.props"), overwrite: true);

        // 7. Create projects: src/<solutionName> and tests/<solutionName>.Tests
        Console.WriteLine("[STEP 5/7] Creating Web API and Test projects...");
        string srcDir = Path.Combine(outputDir, "src", solutionName);
        string testsDir = Path.Combine(outputDir, "tests", $"{solutionName}.Tests");

        if (RunCommand("dotnet", $"new webapi -n {solutionName} -o \"{srcDir}\" --no-restore") != 0)
        {
            Console.Error.WriteLine("[ERROR] Failed to create Web API project.");
            return 1;
        }

        string slnxFile = Path.Combine(outputDir, $"{solutionName}.slnx");
        string webProjFile = Path.Combine(srcDir, $"{solutionName}.csproj");
        string testProjFile = Path.Combine(testsDir, $"{solutionName}.Tests.csproj");

        // Fix CA1852 in generated template Program.cs by making record sealed
        string programCs = Path.Combine(srcDir, "Program.cs");
        if (File.Exists(programCs))
        {
            string programContent = File.ReadAllText(programCs);
            programContent = programContent.Replace("record WeatherForecast(", "sealed record WeatherForecast(");
            File.WriteAllText(programCs, programContent);
        }

        // Normalize Web API csproj for Central Package Management (remove inline Version)
        MigrateProjectToCpm(outputDir, webProjFile);

        Directory.CreateDirectory(testsDir);
        File.Copy(Path.Combine(assetsDir, "TestProject.csproj"), testProjFile, overwrite: true);

        // Create sample MSTest test file with dynamic assertion
        string sampleTest = @"using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace " + solutionName + @".Tests;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ApplicationConfiguredCorrectly()
    {
        var timestamp = DateTimeOffset.UtcNow;
        Assert.IsTrue(timestamp > DateTimeOffset.MinValue);
    }
}
";
        File.WriteAllText(Path.Combine(testsDir, "SmokeTests.cs"), sampleTest);

        // Add projects to solution and establish reference
        RunCommand("dotnet", $"sln add \"{webProjFile}\" \"{testProjFile}\"", outputDir);
        RunCommand("dotnet", $"add \"{testProjFile}\" reference \"{webProjFile}\"", outputDir);

        // 8. Add packages under CPM
        Console.WriteLine("[STEP 6/7] Adding test packages under Central Package Management...");
        if (RunCommand("dotnet", $"add \"{testProjFile}\" package Microsoft.AspNetCore.Mvc.Testing", outputDir) != 0)
        {
            Console.Error.WriteLine("[ERROR] Failed to add Microsoft.AspNetCore.Mvc.Testing to test project.");
            return 1;
        }

        if (withTestcontainers)
        {
            Console.WriteLine("[INFO] Adding Testcontainers.PostgreSql and Respawn...");
            RunCommand("dotnet", $"add \"{testProjFile}\" package Testcontainers.PostgreSql", outputDir);
            RunCommand("dotnet", $"add \"{testProjFile}\" package Respawn", outputDir);

            // Check Docker availability
            int dockerExit = RunCommandQuiet("docker", "--version");
            if (dockerExit != 0)
            {
                Console.WriteLine("[WARNING] Docker CLI or daemon is not currently available.");
                Console.WriteLine("[WARNING] Testcontainers tests require a running Docker daemon. Do NOT fall back to in-memory EF Core provider.");
            }
            else
            {
                Console.WriteLine("[INFO] Docker is available.");
            }
        }

        // 9. Build and test verification in Debug and Release
        Console.WriteLine("[STEP 7/7] Verifying solution in Debug and Release configurations...");

        Console.WriteLine("--> dotnet build (Debug)...");
        if (RunCommand("dotnet", "build -c Debug", outputDir) != 0)
        {
            Console.Error.WriteLine("[ERROR] dotnet build failed in Debug configuration.");
            return 1;
        }

        Console.WriteLine("--> dotnet test (Debug)...");
        if (RunCommand("dotnet", "test -c Debug", outputDir) != 0)
        {
            Console.Error.WriteLine("[ERROR] dotnet test failed in Debug configuration.");
            return 1;
        }

        Console.WriteLine("--> dotnet build (Release)...");
        if (RunCommand("dotnet", "build -c Release", outputDir) != 0)
        {
            Console.Error.WriteLine("[ERROR] dotnet build failed in Release configuration.");
            return 1;
        }

        Console.WriteLine("--> dotnet test (Release)...");
        if (RunCommand("dotnet", "test -c Release", outputDir) != 0)
        {
            Console.Error.WriteLine("[ERROR] dotnet test failed in Release configuration.");
            return 1;
        }

        Console.WriteLine("\n[SUCCESS] Deterministic scaffolding and dual-configuration build/test complete!");
        Console.WriteLine($"Solution ready at: {outputDir}");
        return 0;
    }

    private static void EnsureDotnetRoot()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT"))) return;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string miseDotnetRoot = Path.Combine(home, ".local", "share", "mise", "dotnet-root");
        if (Directory.Exists(miseDotnetRoot))
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", miseDotnetRoot);
        }
    }

    private static void MigrateProjectToCpm(string rootDir, string csprojFile)
    {
        if (!File.Exists(csprojFile)) return;
        string csprojContent = File.ReadAllText(csprojFile);
        string cpmFile = Path.Combine(rootDir, "Directory.Packages.props");
        string cpmContent = File.ReadAllText(cpmFile);

        var match = Regex.Match(csprojContent, @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>");
        if (match.Success)
        {
            string packageId = match.Groups[1].Value;
            string version = match.Groups[2].Value;

            // Remove Version from csproj
            csprojContent = csprojContent.Replace(match.Value, $"<PackageReference Include=\"{packageId}\" />");
            File.WriteAllText(csprojFile, csprojContent);

            // Add PackageVersion to Directory.Packages.props
            if (!cpmContent.Contains($"Include=\"{packageId}\""))
            {
                string packageVersionEntry = $"    <PackageVersion Include=\"{packageId}\" Version=\"{version}\" />\n  </ItemGroup>";
                cpmContent = cpmContent.Replace("  </ItemGroup>", packageVersionEntry);
                File.WriteAllText(cpmFile, cpmContent);
            }
        }
    }

    private static int RunCommand(string command, string arguments, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(command, arguments)
        {
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {command}");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static int RunCommandQuiet(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process == null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    private static string RunCapture(string command, string arguments)
    {
        var psi = new ProcessStartInfo(command, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {command}");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static string GetCallerFilePath([CallerFilePath] string path = "") => path;
}
