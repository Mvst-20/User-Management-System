namespace UserManagementSystem.Components.Shared;

/// <summary>
/// 折线图单条数据系列。
/// <paramref name="Values"/> 与图表的 Labels 一一对应；null 表示该点缺失（折线在此断开）。
/// <paramref name="Color"/> 可选：指定系列颜色（如注销用红色）；缺省时按序取组件调色板。
/// </summary>
public sealed record UcsLineSeries(
    string Name,
    IReadOnlyList<double?> Values,
    string? Color = null);
