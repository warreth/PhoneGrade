using static DeviceService.DeviceService;

using Parsing;
using static CommandExecution.CommandExecution;

string firstDeviceId = "";
var connectedDevices = GetConnectedDevices();
if (IsDeviceConnected())
{
    firstDeviceId = connectedDevices.Values.First();
}
else
{
    System.Console.WriteLine("No device connected");
}
DeviceData deviceData = GetDeviceData(firstDeviceId);


System.Console.WriteLine(deviceData.BatteryHealth);


System.Console.WriteLine(deviceData.Identifier);
System.Console.WriteLine(deviceData.Model);
System.Console.WriteLine(deviceData.Quality);
System.Console.WriteLine(deviceData.PayMethod);


System.Console.WriteLine(deviceData.Storage);
System.Console.WriteLine(deviceData.Color);