namespace ProxyPilot.Core.Engine;

internal static class EditorProcesses
{
    public static bool UsesSni(string? processName) => IsChatEditor(processName);

    public static bool RejectQuic(string? processName) => IsChatEditor(processName);

    private static bool IsChatEditor(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
            return false;
        return processName.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("Cursor.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("codex-code-mode-host.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("codex-command-runner.exe", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
    }
}
