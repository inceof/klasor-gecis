using System.Drawing;
using KlasorGecis;

if (args.Length > 0)
{
    int outcome = 1;
    var thread = new Thread(() => outcome = LiveHarness.Run(args));
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start(); thread.Join();
    return outcome;
}

int failed = 0, passed = 0;
string unitProfile = Path.Combine(Path.GetTempPath(), "KlasorGecis-unit-profile-" + Guid.NewGuid().ToString("N"));
if (!ProfilePaths.Select(new(unitProfile, true, false), out var unitProfileError)) throw new Exception(unitProfileError);
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new Exception("Expected invariant was not satisfied");
        Console.WriteLine($"PASS {name}"); passed++;
    }
    catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.Message}"); failed++; }
}
Check("self token elevation can be inspected", () => PrivilegePolicy.TryIsElevated((uint)Environment.ProcessId, out _));
Check("invalid process identity is rejected", () => !PrivilegePolicy.TryIsElevated(0, out _));
bool Normalizes(string input, string expected) => CoreRules.TryNormalizePath(input, out var p) && p == expected;
Check("literal Turkish path and percent signs survive", () => Normalizes(@"D:\İşler\Çınar #1 %20", @"D:\İşler\Çınar #1 %20"));
Check("file URI is decoded exactly once", () => Normalizes("file:///D:/%C4%B0%C5%9Fler/100%25%20%232/%2520", @"D:\İşler\100% #2\%20"));
Check("drive root stays absolute", () => Normalizes(@"C:\", @"C:\"));
Check("UNC share survives", () => Normalizes(@"\\server\share\Folder", @"\\server\share\Folder"));
Check("UNC file URI retains server", () => Normalizes("file://server/share/Two%20Words", @"\\server\share\Two Words"));
foreach (var bad in new string?[] { null, "", "shell:Downloads", "::{GUID}", "https://example.com/folder", "relative", @"C:relative", @"\rooted", @"\\.\pipe\x", @"\\?\GLOBALROOT\Device", @"\\server", "D:\\bad\npath" })
{
    var input = bad;
    Check($"reject unsupported path {input}", () => !CoreRules.TryNormalizePath(input, out _));
}
Check("bar sits below normal dialog", () => {
    var d = new Rectangle(100,100,1000,700); var w = new Rectangle(0,0,1920,1040);
    var b = CoreRules.PlaceBar(d,w,96); return w.Contains(b) && b.Top >= d.Bottom && b.Width == 500 && b.Height >= 40;
});
Check("bar moves above near lower edge", () => {
    var d = new Rectangle(100,360,1000,660); var w = new Rectangle(0,0,1920,1040);
    var b = CoreRules.PlaceBar(d,w,96); return w.Contains(b) && b.Bottom <= d.Top && b.Width == 500 && b.Height >= 40;
});
Check("negative-coordinate monitor stays bounded", () => {
    var d = new Rectangle(-1800,200,1000,650); var w = new Rectangle(-1920,0,1920,1040);
    var b = CoreRules.PlaceBar(d,w,144); return w.Contains(b) && b.Width == 750;
});
Check("narrow work area does not overflow", () => {
    var w = new Rectangle(-300,40,320,500); var d = new Rectangle(-300,40,320,500);
    var b = CoreRules.PlaceBar(d,w,192); return w.Contains(b) && b.Width > 0 && b.Height > 0;
});
FeatureTests.Run(Check);
InteractionTests.Run(Check);
PreferencesTests.Run(Check);
AutoSwitchTests.Run(Check);
FolderRequestTests.Run(Check);
RefreshPolicyTests.Run(Check);
NativeUiTests.Run(Check);
LocalizationTests.Run(Check);
Console.WriteLine($"RESULT {passed} passed, {failed} failed");
Directory.Delete(unitProfile, true);
return failed == 0 ? 0 : 1;
