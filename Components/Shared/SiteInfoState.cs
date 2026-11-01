namespace UserManagementSystem.Components.Shared;

/// <summary>
/// 站点名称的跨组件同步状态（scoped，每个 circuit 一份）。
/// <para>
/// MainLayout 读取它渲染左侧品牌标题；系统设置页保存成功后调用 <see cref="Set"/>，
/// 布局订阅 <see cref="Changed"/> 后即时刷新，无需整页刷新。
/// </para>
/// </summary>
public sealed class SiteInfoState
{
    /// <summary>当前站点名称；尚未加载时为 UCS（布局的默认品牌名）。</summary>
    public string SiteName { get; private set; } = "UCS";

    /// <summary>是否已成功加载过一次站点信息。</summary>
    public bool Loaded { get; private set; }

    /// <summary>站点名称变更通知。</summary>
    public event Action? Changed;

    /// <summary>更新站点名称；空值回落到默认品牌名。</summary>
    public void Set(string? siteName)
    {
        var value = string.IsNullOrWhiteSpace(siteName) ? "UCS" : siteName.Trim();
        var wasLoaded = Loaded;
        Loaded = true;

        if (wasLoaded && string.Equals(SiteName, value, StringComparison.Ordinal))
        {
            return;
        }

        SiteName = value;
        Changed?.Invoke();
    }
}
