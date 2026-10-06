using PhoneGrade.Core;
using System;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Installing libimobiledevice tools...");
        var progress = new Progress<(int Percent, string Message)>(p => 
            Console.WriteLine($"[{p.Percent}%] {p.Message}"));
        
        bool success = await ToolInstallerService.ExecuteFixAsync("install_idevice_tools", progress);
        Console.WriteLine($"libimobiledevice tools: {(success ? "SUCCESS" : "FAILED")}");
        
        Console.WriteLine("Installing ADB...");
        success = await ToolInstallerService.ExecuteFixAsync("install_adb", progress);
        Console.WriteLine($"ADB: {(success ? "SUCCESS" : "FAILED")}");
        
        Console.WriteLine("Done.");
    }
}