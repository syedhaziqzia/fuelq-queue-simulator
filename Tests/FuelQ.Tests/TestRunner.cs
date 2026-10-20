namespace FuelQ.Tests;

/// <summary>Minimal check runner: prints PASS/FAIL per check and returns a process exit code.</summary>
public sealed class TestRunner
{
    private int _passed, _failed;

    public void Section(string title) => Console.WriteLine($"\n── {title} ──");

    public void Info(string message) => Console.WriteLine($"   ℹ  {message}");

    public void True(string id, string name, bool ok, string detail = "")
    {
        if (ok) _passed++; else _failed++;
        string tag = ok ? "PASS" : "FAIL";
        string suffix = detail.Length > 0 ? $"  ({detail})" : "";
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write($"   {tag}");
        Console.ResetColor();
        Console.WriteLine($"  [{id,-3}] {name}{suffix}");
    }

    public void Near(string id, string name, double actual, double expected, double tolerance) =>
        True(id, name, Math.Abs(actual - expected) <= tolerance, $"got {actual:G6}, expected {expected:G6} ± {tolerance:G3}");

    public void Throws(string id, string name, Action action)
    {
        try { action(); True(id, name, false, "no exception"); }
        catch (ArgumentException) { True(id, name, true); }
    }

    public int Summary()
    {
        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
