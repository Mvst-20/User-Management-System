using Microsoft.AspNetCore.Components;

namespace UserManagementSystem.Components.Shared;

/// <summary>
/// 页头状态（每个 circuit 一份）：把当前页面的标题与操作区提升到顶部信息栏渲染。
/// <para>
/// <see cref="PageContainer"/> 在参数就绪时调用 <see cref="Set"/>；
/// TopBar 订阅 <see cref="Changed"/> 并在顶栏左侧渲染标题与操作按钮。
/// </para>
/// </summary>
public sealed class PageHeaderService
{
    /// <summary>当前页面标题；无活动页面（如登录页）时为 null。</summary>
    public string? Title { get; private set; }

    /// <summary>当前页面操作区（刷新按钮等），由页面以 RenderFragment 提供。</summary>
    public RenderFragment? Actions { get; private set; }

    /// <summary>标题或操作区变化时触发（TopBar 订阅后重渲染）。</summary>
    public event Action? Changed;

    public void Set(string title, RenderFragment? actions)
    {
        if (string.Equals(Title, title, StringComparison.Ordinal) && ReferenceEquals(Actions, actions))
        {
            return;
        }

        Title = title;
        Actions = actions;
        Changed?.Invoke();
    }
}
