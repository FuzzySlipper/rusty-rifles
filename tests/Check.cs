namespace Rifles.Checks;

internal static class Check
{
    private static readonly List<string> failures = [];
    private static string group = "startup";
    private static int groups;
    private static int assertions;

    internal static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) failures.Add($"{group}: {message}");
    }

    internal static void Rejected(Action action, string message, string? fragment = null)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            Require(fragment is null || error.Message.Contains(fragment, StringComparison.Ordinal),
                $"{message}: expected '{fragment}', got '{error.Message}'");
            return;
        }
        Require(false, message);
    }

    internal static void Run(string name, Action action)
    {
        string previous = group;
        group = name;
        groups++;
        int before = failures.Count;
        try { action(); }
        catch (Exception error) { failures.Add($"{name}: {error}"); }
        Console.WriteLine($"{name}: {(before == failures.Count ? "PASS" : "FAIL")}");
        group = previous;
    }

    internal static int Finish()
    {
        foreach (string failure in failures) Console.Error.WriteLine(failure);
        Console.WriteLine($"Checks: {groups} groups, {assertions} assertions, {failures.Count} failures.");
        return failures.Count == 0 ? 0 : 1;
    }
}
