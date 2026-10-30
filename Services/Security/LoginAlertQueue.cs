using System.Threading.Channels;

namespace UserManagementSystem.Services.Security;

/// <summary>登录告警消息（入队后由后台服务异步发送，绝不阻塞登录）。</summary>
public sealed record LoginAlertMessage(
    ulong UserId,
    string Username,
    string? Email,
    IReadOnlyList<string> RiskKinds,
    string DeviceName,
    string? Ip,
    string? Location,
    DateTime TimeUtc);

/// <summary>登录告警队列（有界、写满即丢，保证登录主流程零阻塞）。</summary>
public interface ILoginAlertQueue
{
    /// <summary>尝试入队；队列已满时返回 false（丢弃并记录日志）。</summary>
    bool TryEnqueue(LoginAlertMessage message);

    /// <summary>供后台消费服务读取。</summary>
    ChannelReader<LoginAlertMessage> Reader { get; }
}

/// <inheritdoc cref="ILoginAlertQueue"/>
public class LoginAlertQueue : ILoginAlertQueue
{
    private const int Capacity = 500;

    private readonly Channel<LoginAlertMessage> _channel;
    private readonly ILogger<LoginAlertQueue> _logger;

    public LoginAlertQueue(ILogger<LoginAlertQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<LoginAlertMessage>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<LoginAlertMessage> Reader => _channel.Reader;

    public bool TryEnqueue(LoginAlertMessage message)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        _logger.LogWarning(
            "登录告警队列已满，丢弃提醒：user={UserId} risks={Risks} ip={Ip}",
            message.UserId, string.Join(',', message.RiskKinds), message.Ip);
        return false;
    }
}
