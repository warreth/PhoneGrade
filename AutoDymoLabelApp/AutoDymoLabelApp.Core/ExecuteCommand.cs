using System;
using System.Diagnostics;
using System.Text;

namespace CommandExecution
{
    public static class CommandExecution
    {
    /// <summary>
    /// Executes a system command and returns the output.
    /// </summary>
    public static string ExecuteCommand(string command, string arguments)
    {
        try
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            string result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrWhiteSpace(result))
            {
                return "NO OUTPUT";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
    }
}