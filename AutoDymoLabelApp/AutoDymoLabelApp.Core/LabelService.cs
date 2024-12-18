// Handles label generation

public class LabelService
{
    private string templatePath = "Assets/my.dymo";
    private string outputPath = "Assets/gen_label.dymo";

    public void GenerateLabel(DeviceData data)
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
