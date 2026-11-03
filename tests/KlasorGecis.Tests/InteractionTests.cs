using System.Reflection;
using KlasorGecis;

internal static class InteractionTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("input monitor cannot acknowledge an unobserved zero batch", () => OnSta(() =>
        {
            using var input = new InputMonitor();
            return input.Available && !input.Received(0);
        }));
        check("selection capture retains 32-bit offsets above 65535", () => OnSta(() =>
        {
            using var box = new TextBox { Multiline = true, MaxLength = 200000, Text = new string('x', 100000) };
            _ = box.Handle; box.Select(70000, 17000);
            var method = typeof(Native).GetMethod("TryReadSelection", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new Exception("Missing 32-bit selection capture.");
            object?[] values = [box.Handle, 0, 0];
            return (bool)method.Invoke(null, values)! && (int)values[1]! == 70000 && (int)values[2]! == 87000;
        }));
        check("selection capture leaves Turkish and emoji text untouched", () => OnSta(() =>
        {
            using var box = new TextBox { Text = "Çınar 🌳 İğde" };
            _ = box.Handle; box.Select(6, 2);
            var method = typeof(Native).GetMethod("TryReadSelection", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new Exception("Missing selection capture.");
            object?[] values = [box.Handle, 0, 0];
            return (bool)method.Invoke(null, values)! && (int)values[1]! == 6 && (int)values[2]! == 8 && box.Text == "Çınar 🌳 İğde";
        }));
    }

    internal static bool OnSta(Func<bool> action)
    {
        bool result = false; Exception? error = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
        return result;
    }
}
