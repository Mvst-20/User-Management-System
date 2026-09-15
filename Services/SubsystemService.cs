using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Exceptions;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 子系统凭证实现：ApiKey 明文可查，ApiSecret 仅存 SHA256 哈希（参照 RefreshToken 的既有做法）。
/// </summary>
public sealed class SubsystemService : ISubsystemService
{
    /// <summary>最近使用时间的写入节流窗口。</summary>
    private static readonly TimeSpan TouchThrottle = TimeSpan.FromMinutes(1);

    private readonly ApplicationDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly ILogger<SubsystemService> _logger;

    public SubsystemService(ApplicationDbContext context, IPermissionService permissionService, ILogger<SubsystemService> logger)
    {
        _context = context;
        _permissionService = permissionService;
        _logger = logger;
    }

    public async Task<(Subsystem Entry, string PlainSecret)> CreateAsync(CreateSubsystemRequest request, ulong operatorId, CancellationToken ct = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessException(ResultCodes.ValidationError, "子系统名称不能为空");
        if (string.IsNullOrWhiteSpace(code))
            throw new BusinessException(ResultCodes.ValidationError, "子系统标识不能为空");

        if (await _context.Subsystems.AnyAsync(s => s.Code == code, ct))
            throw new BusinessException(ResultCodes.Error, "子系统标识已存在");

        var (apiKey, plainSecret, secretHash) = GenerateCredential();

        var entry = new Subsystem
        {
            Name = name,
            Code = code,
            Description = request.Description?.Trim(),
            HomeUrl = request.HomeUrl?.Trim(),
            IsEnabled = true,
            ApiKey = apiKey,
            ApiSecretHash = secretHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = operatorId,
            ExpiresAt = request.ExpiresAt
        };

        _context.Subsystems.Add(entry);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("[SUBSYSTEM] Created subsystem {Code} (id={Id}) by user {UserId}", entry.Code, entry.Id, operatorId);

        return (entry, plainSecret);
    }

    public async Task<(Subsystem Entry, string PlainSecret)?> RegenerateSecretAsync(ulong id, ulong operatorId, CancellationToken ct = default)
    {
        var entry = await _context.Subsystems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entry is null)
            return null;

        var (apiKey, plainSecret, secretHash) = GenerateCredential();

        entry.ApiKey = apiKey;
        entry.ApiSecretHash = secretHash;
        entry.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogWarning("[SUBSYSTEM] Credential regenerated for {Code} (id={Id}) by user {UserId}", entry.Code, entry.Id, operatorId);

        return (entry, plainSecret);
    }

    public Task<Subsystem?> GetByIdAsync(ulong id, CancellationToken ct = default)
        => _context.Subsystems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Subsystem?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _context.Subsystems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct);

    public async Task<(List<Subsystem> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _context.Subsystems.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(s => s.Name.Contains(keyword) || s.Code.Contains(keyword));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<Subsystem?> UpdateAsync(ulong id, UpdateSubsystemRequest request, ulong operatorId, CancellationToken ct = default)
    {
        var entry = await _context.Subsystems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entry is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Name))
            entry.Name = request.Name.Trim();
        if (request.Description is not null)
            entry.Description = request.Description.Trim();
        if (request.HomeUrl is not null)
            entry.HomeUrl = request.HomeUrl.Trim();
        if (request.ExpiresAt.HasValue)
            entry.ExpiresAt = request.ExpiresAt;

        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("[SUBSYSTEM] Updated {Code} (id={Id}) by user {UserId}", entry.Code, entry.Id, operatorId);

        return entry;
    }

    public async Task<Subsystem?> SetStatusAsync(ulong id, bool isEnabled, ulong operatorId, CancellationToken ct = default)
    {
        var entry = await _context.Subsystems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entry is null)
            return null;

        entry.IsEnabled = isEnabled;
        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("[SUBSYSTEM] {Code} (id={Id}) {State} by user {UserId}",
            entry.Code, entry.Id, isEnabled ? "enabled" : "disabled", operatorId);

        return entry;
    }

    public async Task<bool> DeleteAsync(ulong id, CancellationToken ct = default)
    {
        var entry = await _context.Subsystems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entry is null)
            return false;

        // 级联删除前先反查受影响用户（其所属用户组持有该子系统注册的权限），
        // 删除落库后这些权限消失，必须失效对应缓存，否则已登录用户最长 5 分钟内仍持旧权限
        var permissionIds = await _context.Permissions
            .Where(p => p.SubsystemId == id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var affectedUserIds = permissionIds.Count == 0
            ? new List<ulong>()
            : await _context.UserGroupPermissions
                .Where(gp => permissionIds.Contains(gp.PermissionId))
                .Join(_context.UserGroupMembers,
                    gp => gp.GroupId,
                    member => member.GroupId,
                    (gp, member) => member.UserId)
                .Distinct()
                .ToListAsync(ct);

        _context.Subsystems.Remove(entry);
        await _context.SaveChangesAsync(ct);
        await _permissionService.InvalidateUsersPermissionCacheAsync(affectedUserIds);

        _logger.LogWarning(
            "[SUBSYSTEM] Deleted {Code} (id={Id}), cascaded permissions={Permissions}, cache invalidated users={Users}",
            entry.Code, entry.Id, permissionIds.Count, affectedUserIds.Count);

        return true;
    }

    public async Task<Subsystem?> ValidateCredentialAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            return null;

        var entry = await _context.Subsystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ApiKey == apiKey, ct);

        if (entry is null)
            return null;

        if (!entry.IsEnabled)
        {
            _logger.LogWarning("[SUBSYSTEM] Rejected credential for disabled subsystem {Code}", entry.Code);
            return null;
        }

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow)
        {
            _logger.LogWarning("[SUBSYSTEM] Rejected expired credential for subsystem {Code}", entry.Code);
            return null;
        }

        if (!SecretMatches(apiSecret, entry.ApiSecretHash))
        {
            _logger.LogWarning("[SUBSYSTEM] Invalid secret for subsystem {Code}", entry.Code);
            return null;
        }

        return entry;
    }

    public async Task TouchAsync(ulong id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = await _context.Subsystems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entry is null)
            return;

        // 节流：避免每个携带凭证的请求都写库
        if (entry.LastUsedAt.HasValue && now - entry.LastUsedAt.Value < TouchThrottle)
            return;

        entry.LastUsedAt = now;
        await _context.SaveChangesAsync(ct);
    }

    // ==================== 内部 ====================

    private static (string ApiKey, string PlainSecret, string SecretHash) GenerateCredential()
    {
        var apiKey = "ucs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var plainSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return (apiKey, plainSecret, HashSecret(plainSecret));
    }

    private static string HashSecret(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static bool SecretMatches(string providedSecret, string storedHash)
    {
        var computed = HashSecret(providedSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
