using static CommandExecution.CommandExecution;

public class ActivationService
{
    public string SkipActivation()
    {
        try
        {
            ExecuteCommand("ideviceactivation", "activate -b");
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
