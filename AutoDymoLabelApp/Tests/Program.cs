using AutoDymoLabel.Core;
using AutoDymoLabel.Core.Diagnostics;

// Assert-based self-check for the label pipeline: parsers, mappers, panic rules.
// Run: dotnet run --project Tests
int failures = 0;
void Check(bool cond, string name)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}");
    if (!cond) failures++;
}

// --- Parsers.ParseBatteryHealth ---
string batteryPlist = """
    <key>DesignCapacity</key><integer>3227</integer>
    <key>AppleRawMaxCapacity</key><integer>2900</integer>
    """;
Check(Parsers.ParseBatteryHealth(batteryPlist) == "90", "battery health 2900/3227 = 90%");
Check(Parsers.ParseBatteryHealth("") == "NOBATT", "empty plist → NOBATT");
Check(Parsers.ParseBatteryHealth("<key>DesignCapacity</key><integer>0</integer>") == "NOBATT", "zero design capacity → NOBATT");

// --- Parsers.ParseIdentifier ---
Check(Parsers.ParseIdentifier("356938035643809", "F2LX9") == "356938035643809", "IMEI used when valid");
Check(Parsers.ParseIdentifier("NO OUTPUT", "F2LX9ABCD") == "F2LX9ABCD", "serial fallback when no IMEI");
Check(Parsers.ParseIdentifier("", "") == "NOID", "both empty → NOID");

// --- Parsers.KeyValue ---
Check(Parsers.KeyValue("TotalDiskCapacity: 255501272064\n", "TotalDiskCapacity") == "255501272064", "key-value parse");

// --- Mappers ---
Check(Mappers.MapModel("iPhone14,2") == "13Pro", "model mapping iPhone14,2");
Check(Mappers.MapModel("iPhone99,9") == "iPhone99,9", "unknown model falls back to raw");
Check(Mappers.MapColor("#ffffff") == "Wit", "color hex mapping");
Check(Mappers.MapColor("3") == "Goud", "color numeric mapping");
Check(Mappers.MapStorage(255_501_272_064) == "256GB", "256 GB bucket");
Check(Mappers.MapStorage(1_000_000_000_000) == "1TB", "1 TB bucket");
Check(Mappers.MapStorage(30_000_000_000) == "32GB", "32 GB bucket");

// --- PanicRules: real-world panic strings ---
string thermalLog = """
    panic(cpu 0 caller 0xfffffff02abe87e8): userspace watchdog timeout: no successful checkins from thermalmonitord since wake
    service returned not alive with context : is_alive_func returned unhealthy : current fffffffffffc, mask 7fffffffffff, expected 7fffffffffff. SD: 0 BC: 1 Missing sensor(s): TG0B TG0V
    """;
var issues = PanicRules.Match(thermalLog);
Check(issues.Any(i => i.Title.Contains("TG0B")), "TG0B detected");
Check(issues.Any(i => i.Title.Contains("TG0V")), "TG0V detected");
Check(issues.Any(i => i.Title.Contains("Thermal")), "thermalmonitord detected");

string micLog = "panic(cpu 0): Missing sensor(s): Mic1";
Check(PanicRules.Match(micLog).Any(i => i.Title.Contains("Mic1")), "Mic1 detected");

string ansLog = "panic(cpu 1): ANS2 Recoverable Panic boot failure";
Check(PanicRules.Match(ansLog).Any(i => i.Title.Contains("NAND")), "ANS2/NAND detected");

string aopLog = "AOP PANIC: SCMto: 0 - prox";
Check(PanicRules.Match(aopLog).Any(i => i.Title.Contains("proximity")), "AOP proximity detected");

string okLog = "no panic here, just a normal sysdiagnose";
Check(PanicRules.Match(okLog).Count == 0, "clean log → no findings");

// Dedupe: same rule shouldn't fire twice
var deduped = PanicRules.Match(thermalLog + thermalLog).Dedupe();
Check(deduped.Count == PanicRules.Match(thermalLog).Count, "dedupe collapses repeated findings");

// --- Label generation (template copy) ---
string template = Path.Combine(Path.GetTempPath(), $"my-{Guid.NewGuid():N}.dymo");
File.WriteAllText(template, "ID=IDENTIFIER M=MODEL C=PCOLOR B=BATTERY Q=QUALITY P=PAYM S=STORAGE");
LabelService.ConfiguredTemplatePath = template;
var path = LabelService.GenerateLabel(new DeviceData
{
    Identifier = "356938035643809",
    Model = "13Pro",
    Color = "Wit",
    BatteryHealth = "90",
    Quality = "A",
    PayMethod = "Marge",
    Storage = "256GB",
});
string generated = File.ReadAllText(path);
Check(generated == "ID=356938035643809 M=13Pro C=Wit B=90% Q=A P=Marge S=256GB", "label placeholders replaced");
File.Delete(template);

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
return failures;
