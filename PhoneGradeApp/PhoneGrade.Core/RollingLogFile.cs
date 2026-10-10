using System.Text;

namespace PhoneGrade.Core;

/// <summary>
/// A log file that keeps itself under a size cap by rolling to one older file.
/// </summary>
internal static class RollingLogFile
{
    /// <summary>
    /// Appends text to the file, moving the current file to its .1 name first
    /// once it has grown past the cap. The directory is created here, so a
    /// caller does not have to.
    /// </summary>
    public static void Append(string path, string text, long maxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
        {
            string oldPath = path + ".1";
            File.Delete(oldPath);
            File.Move(path, oldPath);
        }

        File.AppendAllText(path, text, Encoding.UTF8);
    }
}
