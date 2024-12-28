using static CommandExecution.CommandExecution;
namespace Activation;
public static class ActivationService
{
    public static string SkipActivation(string deviceId)
    {
        try
        {
            ExecuteCommand("ideviceactivation", $"-u {deviceId} activate -b");
            return "Device activated";
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
