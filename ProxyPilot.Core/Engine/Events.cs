using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public sealed class ConnectionEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Time { get; init; } = DateTime.Now;
    public string ProcessName { get; init; } = "";
    public int Pid { get; init; }
    public string Target { get; init; } = "";
    public string RuleName { get; init; } = "";
    public RuleAction Action { get; init; }
    public string ProxyName { get; init; } = "";
    public string Status { get; set; } = "";
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

public sealed class EngineLogEvent
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
}
