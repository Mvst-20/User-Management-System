namespace UserManagementSystem.DTOs;

/// <summary>
/// 子系统视图。<see cref="ApiKey"/> 为公开标识（可展示）；
/// Secret 永不回显，仅在创建/重置时一次性返回。
/// </summary>
public record SubsystemResponse(
    ulong Id,
    string Name,
    string Code,
    string? Description,
    string? HomeUrl,
    bool IsEnabled,
    string ApiKey,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ulong? CreatedBy,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt);

/// <summary>
/// 创建子系统请求。
/// <para>子系统权限码改由子系统凭凭证自注册（PUT /api/subsystems/self/permissions），
/// 管理员登记时不再代为声明。</para>
/// </summary>
public record CreateSubsystemRequest(
    string Name,
    string Code,
    string? Description = null,
    string? HomeUrl = null,
    DateTime? ExpiresAt = null);

/// <summary>更新子系统请求（仅可改元数据与有效期，凭证不变）。</summary>
public record UpdateSubsystemRequest(
    string? Name = null,
    string? Description = null,
    string? HomeUrl = null,
    DateTime? ExpiresAt = null);

/// <summary>启用/停用子系统。</summary>
public record SetSubsystemStatusRequest(
    bool IsEnabled
);

/// <summary>
/// 创建/重置凭证的响应：明文 Secret 仅在此刻返回一次，务必立即保存。
/// </summary>
public record SubsystemCredentialResponse(
    SubsystemResponse Subsystem,
    string ApiSecret);

// ============ 子系统权限自注册 ============

/// <summary>子系统自注册的单个权限码声明。</summary>
public record SubsystemPermissionItem(
    string Code,
    string? Name = null,
    string? Description = null);

/// <summary>
/// 子系统凭凭证自注册权限的请求（PUT 全量同步语义）：
/// 清单外、原归属本子系统的权限会被删除。
/// </summary>
public record SubsystemPermissionSyncRequest(
    List<SubsystemPermissionItem>? Codes = null);

/// <summary>子系统权限自注册同步的响应。</summary>
public record SubsystemPermissionSyncResponse(
    int Added,
    int Updated,
    int Deleted,
    List<string> Conflicts);
