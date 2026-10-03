using System;
using System.IO;
using PhoneGrade.Core.SecurityServices;
using PhoneGrade.Tests;

namespace Tests;

/// <summary>
/// Where the live imei.info tests get their key from.
///
/// The key is read at run time so it never has to sit in the repository: CI
/// passes IMEI_INFO_API_KEY as an environment variable, and a local run reads
/// PhoneGradeApp/Tests/.imei-info-key, which is ignored by git. Without either
/// of them the live facts report themselves as skipped rather than quietly
/// passing.
/// </summary>
internal static class ImeiInfoLiveKey
{
    public const string EnvironmentVariable = "IMEI_INFO_API_KEY";

    /// <summary>Repository relative file the key is read from when there is no environment variable.</summary>
    public static readonly string[] FilePath = { "PhoneGradeApp", "Tests", ".imei-info-key" };

    private static readonly object Gate = new();
    private static string? _key;
    private static (bool Ok, decimal Balance, string? Error)? _balance;

    /// <summary>The key to run with, or null when the caller did not provide one.</summary>
    public static string? TryRead()
    {
        lock (Gate)
        {
            if (_key is not null)
                return _key;

            string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return _key = fromEnvironment.Trim();

            try
            {
                string path = RepoPath.Get(FilePath);
                if (File.Exists(path))
                {
                    string fromFile = File.ReadAllText(path).Trim();
                    if (fromFile.Length > 0)
                        return _key = fromFile;
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Outside a checkout, so there is no file to fall back to.
            }

            return null;
        }
    }

    /// <summary>
    /// The account balance, read once per run. Facts that only make sense in
    /// one account state use it to skip with a reason instead of failing on a
    /// condition they cannot change.
    /// </summary>
    public static (bool Ok, decimal Balance, string? Error) Balance(string key)
    {
        lock (Gate)
        {
            if (_balance is null)
                _balance = ImeiInfoApiService.GetBalanceAsync(key).GetAwaiter().GetResult();

            return _balance.Value;
        }
    }
}
