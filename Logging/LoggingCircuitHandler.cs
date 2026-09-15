using Microsoft.AspNetCore.Components.Server.Circuits;

namespace UserManagementSystem.Logging;

/// <summary>
/// 记录管理前端 circuit 的建立与关闭。
/// <para>
/// 排障用途：若浏览器中出现"点击无响应"，而日志里<b>没有任何 circuit 记录</b>，
/// 即可判定问题位于前端脚本或反向代理的 WebSocket / SSE 通道，而非应用逻辑；
/// 反之若 circuit 已建立，则应从 API 调用日志继续排查。
/// </para>
/// </summary>
public sealed class LoggingCircuitHandler : CircuitHandler
{
    private readonly ILogger<LoggingCircuitHandler> _logger;

    public LoggingCircuitHandler(ILogger<LoggingCircuitHandler> logger) => _logger = logger;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[UI] circuit established: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[UI] circuit closed: {CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }
}
