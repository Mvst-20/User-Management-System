using UserManagementSystem.DTOs;
using UserManagementSystem.Services;

namespace UserManagementSystem.Extensions;

/// <summary>
/// 权限级别的统一校验：确保操作者只能管理"级别严格低于自己"的用户。
/// <para>
/// 级别取自用户所属权限组中最小的 <c>level</c>（<strong>数值越小权限越高</strong>），
/// 详见 <see cref="PermissionCodes.GroupLevel"/>。
/// </para>
/// </summary>
public static class PrivilegeLevelExtensions
{
    /// <summary>
    /// 校验当前登录者是否有权对目标用户执行写操作。
    /// </summary>
    /// <returns>允许时返回 <c>null</c>；拒绝时返回应直接写出的 <see cref="IResult"/>。</returns>
    public static async Task<IResult?> ValidatePrivilegeLevelAsync(
        this HttpContext httpContext,
        IUserGroupService userGroupService,
        ulong targetUserId)
    {
        var actorId = httpContext.User.GetUserId();
        if (actorId is null)
        {
            return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
        }

        // 操作自己不参与级别比较（由各端点的既有规则处理，例如状态仅管理员可改）
        if (actorId.Value == targetUserId)
        {
            return null;
        }

        var actorLevel = await userGroupService.GetUserPrivilegeLevelAsync(actorId.Value);
        var targetLevel = await userGroupService.GetUserPrivilegeLevelAsync(targetUserId);

        // 目标级别数值不大于自身 → 目标权限不低于自己 → 拒绝
        if (targetLevel <= actorLevel)
        {
            return Results.Json(new ApiResponse(ResultCodes.UpdateUserFail_PrivilegeLevel,
                $"目标用户的权限级别不低于你（目标 {Describe(targetLevel)}，自身 {Describe(actorLevel)}），无法操作"),
                statusCode: 403);
        }

        return null;
    }

    /// <summary>
    /// 校验操作者能否分配/操作某个权限组：目标组级别必须严格低于自己（数值更大）。
    /// </summary>
    public static bool CanManageGroup(int actorLevel, int groupLevel) => groupLevel > actorLevel;

    private static string Describe(int level) => level == PermissionCodes.GroupLevel.NoGroup
        ? "无权限组"
        : level.ToString();
}
