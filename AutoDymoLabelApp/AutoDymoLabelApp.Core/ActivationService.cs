using static CommandExecution.CommandExecution;
namespace Activation;
public static class ActivationService
{
    public static string SkipActivation(string deviceId)
    {
        try
        {
            System.Console.WriteLine($"Activating device: {deviceId}");
            return ExecuteCommand("ideviceactivation", $"-u {deviceId} activate -b");
        }
        catch(Exception ex)
        {
            if (ex.Message.Contains("drmHandshake")) 
            { 
                return "Please connect to the internet and try again"; 
            } 
            else
            {
                return ex.Message;
            }
        }

    }
}
