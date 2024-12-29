// Handles label generation
using System.Diagnostics;

public static class LabelService
{
    static string templatePath = "../Assets/my.dymo";
    public static string outputPath = "../Assets/gen_label.dymo";
    public static void GenerateLabel(DeviceData data)
    {
        File.Copy(templatePath, outputPath, true);

        var content = File.ReadAllText(outputPath);
        
        content = content
            .Replace("IDENTIFIER", data.Identifier)
            .Replace("MODEL", data.Model)
            .Replace("PCOLOR", data.Color)
            .Replace("BATTERY", data.BatteryHealth)
            .Replace("QUALITY", data.Quality)
            .Replace("PAYM", data.PayMethod)
            .Replace("STORAGE", data.Storage);

        File.WriteAllText(outputPath, content);
    }
    
}

public static class OpenLabel
{
    public static string OpenLabelFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LabelService.outputPath,
                UseShellExecute = true // Ensure it's opened with the associated default application
            });
            return "Succesfully opened label file";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
