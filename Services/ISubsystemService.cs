using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 子系统凭证服务：签发、校验、启停与最近使用记录。
/// </summary>
public interface ISubsystemService
{
    /// <summary>创建子系统并签发凭证（返回明文 Secret，仅此一次）。</summary>
    Task<(Subsystem Entry, string PlainSecret)> CreateAsync(CreateSubsystemRequest request, ulong operatorId, CancellationToken ct = default);

    /// <summary>重新生成密钥（ApiKey 与 Secret 同时更换）；子系统不存在时返回 null。</summary>
    Task<(Subsystem Entry, string PlainSecret)?> RegenerateSecretAsync(ulong id, ulong operatorId, CancellationToken ct = default);

    Task<Subsystem?> GetByIdAsync(ulong id, CancellationToken ct = default);

    Task<Subsystem?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<(List<Subsystem> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);

    Task<Subsystem?> UpdateAsync(ulong id, UpdateSubsystemRequest request, ulong operatorId, CancellationToken ct = default);

    Task<Subsystem?> SetStatusAsync(ulong id, bool isEnabled, ulong operatorId, CancellationToken ct = default);

    Task<bool> DeleteAsync(ulong id, CancellationToken ct = default);

    /// <summary>
    /// 校验凭证：需 ApiKey 匹配、Secret 哈希一致、已启用且未过期。失败返回 null。
    /// </summary>
    Task<Subsystem?> ValidateCredentialAsync(string apiKey, string apiSecret, CancellationToken ct = default);

    /// <summary>记录最近使用时间（内部做节流，避免每个请求都写库）。</summary>
    Task TouchAsync(ulong id, CancellationToken ct = default);
}
