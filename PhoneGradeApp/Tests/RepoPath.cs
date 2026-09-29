using System;
using System.IO;

namespace PhoneGrade.Tests;

/// <summary>
/// Resolves paths in the checked-out repository from the test output directory.
/// The tests that inspect source files run from bin/, where relative paths like
/// "PhoneGradeApp/..." would otherwise point at a folder that does not exist.
/// </summary>
internal static class RepoPath
{
    private static readonly Lazy<string> RootLocator = new(FindRoot);

    /// <summary>Absolute path of the repository root.</summary>
    public static string Root => RootLocator.Value;

    /// <summary>Combines a repository relative path with the repository root.</summary>
    public static string Get(params string[] parts)
    {
        var path = Root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }
        return path;
    }

    /// <summary>Reads a repository file as text.</summary>
    public static string Read(params string[] parts) => File.ReadAllText(Get(parts));

    private static string FindRoot()
    {
        // The marker is the solution folder; walk up from the test assembly so the
        // lookup works from any build configuration or test host working directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PhoneGradeApp", "PhoneGrade.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'. " +
            "Expected to find PhoneGradeApp/PhoneGrade.sln in a parent directory.");
    }
}
