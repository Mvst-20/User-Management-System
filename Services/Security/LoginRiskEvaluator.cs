using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services.Security;

/// <summary>风险类型。</summary>
public static class LoginRiskKinds
{
    public const string NewDevice = "new_device";
    public const string NewLocation = "new_location";
    public const string NewNetwork = "new_network";
}

/// <summary>
/// 登录风险评估：判定一次登录是否来自「新设备」或「异地」。
/// <para>
/// 历史依据为 <c>user_sessions</c> 中该用户近期（<c>Security:LocationHistoryDays</c>）的登录记录，
/// 判定发生在创建新会话之前，因此不会被本次登录自身污染。
/// </para>
/// </summary>
public interface ILoginRiskEvaluator
{
    /// <summary>返回本次登录命中的风险类型（空表示与历史一致，无需提醒）。</summary>
    Task<IReadOnlyList<string>> EvaluateAsync(ulong userId, DeviceInfo device, CancellationToken ct = default);
}

/// <inheritdoc cref="ILoginRiskEvaluator"/>
public class LoginRiskEvaluator : ILoginRiskEvaluator
{
    private readonly ApplicationDbContext _context;
    private readonly IIpLocationResolver _locations;
    private readonly AppConfiguration _appConfig;

    public LoginRiskEvaluator(ApplicationDbContext context, IIpLocationResolver locations, AppConfiguration appConfig)
    {
        _context = context;
        _locations = locations;
        _appConfig = appConfig;
    }

    public async Task<IReadOnlyList<string>> EvaluateAsync(ulong userId, DeviceInfo device, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _appConfig.Security.LocationHistoryDays));

        var history = await _context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CreatedAt >= cutoff)
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .Select(s => new { s.DeviceKey, s.Location, s.Ip })
            .ToListAsync(ct);

        // 首次登录（或长期未登录导致历史为空）：一切皆"新"，不打扰用户
        if (history.Count == 0)
        {
            return Array.Empty<string>();
        }

        var risks = new List<string>(2);

        if (!history.Any(h => h.DeviceKey == device.Key))
        {
            risks.Add(LoginRiskKinds.NewDevice);
        }

        if (device.Location != Ip2RegionLocationResolver.Unknown)
        {
            if (!history.Any(h => h.Location == device.Location))
            {
                risks.Add(LoginRiskKinds.NewLocation);
            }
        }
        else
        {
            // 无离线库时的降级判定：同一 /24 网段内视为熟悉地点
            var networkKey = _locations.NetworkKey(device.Ip);
            if (networkKey != null && !history.Any(h => _locations.NetworkKey(h.Ip) == networkKey))
            {
                risks.Add(LoginRiskKinds.NewNetwork);
            }
        }

        return risks;
    }
}
