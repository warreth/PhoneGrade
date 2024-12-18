 // Represents the data retrieved from the connected device.
 
 public class DeviceData
     {
         public string Identifier { get; set; } = "NOID"; //the serialnumber or the imei
         public string BatteryHealth { get; set; } = "NOBATT";
         public string Color { get; set; } = "NOCOLOR";
         public string Storage { get; set; } = "NO STORAGE";
         public string Model { get; set; } = "NO MODEL";
         public string Quality { get; set; } = "NO QUALITY";
         public string PayMethod { get; set; } = "NO PAYMENTMETHOD";
     }