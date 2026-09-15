using UserManagementSystem.DTOs;

namespace UserManagementSystem.Auth;

/// <summary>
/// 当前登录用户的"有效权限码"集合（scoped，每个 circuit 一份）。
/// 有效权限由服务端展开（含 A/B/C 特殊权限的覆盖效果），
/// 前端仅据此控制菜单与按钮显隐；真正的鉴权仍由 API 端点执行。
/// </summary>
public sealed class PermissionState
{
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);

    /// <summary>权限集合变更通知。</summary>
    public event Action? Changed;

    /// <summary>是否已完成一次加载。</summary>
    public bool Loaded { get; private set; }

    /// <summary>当前有效权限码集合。</summary>
    public IReadOnlyCollection<string> Codes => _codes;

    /// <summary>
    /// 当前用户的最高权限级别（数值越小权限越高）。
    /// 用于前端判断"能否管理某个用户/权限组"。
    /// </summary>
    public int Level { get; private set; } = PermissionCodes.GroupLevel.NoGroup;

    /// <summary>设置有效权限集合与权限级别。</summary>
    public void Set(IEnumerable<string> codes, int level = PermissionCodes.GroupLevel.NoGroup)
    {
        _codes.Clear();
        foreach (var code in codes)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                _codes.Add(code);
            }
        }

        Level = level;
        Loaded = true;
        Changed?.Invoke();
    }

    /// <summary>清空权限（登出或加载失败时调用）。</summary>
    public void Clear()
    {
        var had = _codes.Count > 0;
        _codes.Clear();
        Level = PermissionCodes.GroupLevel.NoGroup;
        Loaded = false;
        if (had)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>能否管理指定级别的权限组：目标级别必须严格低于自己（数值更大）。</summary>
    public bool CanManageGroup(int groupLevel) => groupLevel > Level;

    /// <summary>是否拥有指定权限；传入空权限码视为"无需权限"，恒为 true。</summary>
    public bool Has(string? code) => string.IsNullOrWhiteSpace(code) || _codes.Contains(code);

    /// <summary>是否拥有其中任意一项权限。</summary>
    public bool HasAny(params string?[] codes) => codes.Any(Has);

    /// <summary>是否拥有全部权限。</summary>
    public bool HasAll(params string?[] codes) => codes.All(Has);
}
