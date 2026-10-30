namespace UserManagementSystem.Services;

/// <summary>
/// 缓存版本号服务：为「键带版本前缀」的缓存提供跨实例一致的失效信号。
/// <para>
/// 读取路径被 <see cref="Configuration.PermissionsConfig.VersionCacheSeconds"/>（默认 1 秒）
/// 的内存缓存吸收，稳态下不产生额外数据库压力；写路径为单条 UPSERT，天然原子。
/// </para>
/// </summary>
public interface ICacheVersionService
{
    /// <summary>
    /// 读取指定作用域的当前版本号。读取失败或作用域未初始化时返回 0（降级），
    /// 调用方应把 0 视为"无法感知失效"，采用较短 TTL 兜底。
    /// </summary>
    Task<long> GetVersionAsync(string scope, CancellationToken ct = default);

    /// <summary>
    /// 递增指定作用域的版本号并立即刷新本实例的内存值，返回递增后的版本号。
    /// 任何影响该作用域缓存的写操作落库后都应调用一次。
    /// </summary>
    Task<long> BumpAsync(string scope, CancellationToken ct = default);
}
