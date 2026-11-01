using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Extensions;

/// <summary>
/// Minimal API 端点权限检查扩展
/// </summary>
public static class PermissionEndpointExtensions
{
    /// <summary>
    /// 为端点添加权限检查
    /// </summary>
    /// <param name="builder">路由构建器</param>
    /// <param name="permissionCode">所需权限代码</param>
    /// <param name="allowSelf">是否允许访问自己的资源时跳过权限检查（路由参数需包含 {id}）</param>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permissionCode, bool allowSelf = false)
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var userId = httpContext.User.GetUserId();

            if (!userId.HasValue)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            // allowSelf: 通过路由数据查找 id 参数，匹配当前用户则跳过权限检查
            if (allowSelf)
            {
                var routeIdObj = httpContext.GetRouteValue("id");
                if (routeIdObj != null && ulong.TryParse(routeIdObj.ToString(), out var routeId) && routeId == userId.Value)
                {
                    return await next(context);
                }
            }

            var permissionService = httpContext.RequestServices.GetRequiredService<IPermissionService>();

            // 鉴权查询设置 3 秒超时：数据库不可用时快速回落到最近权限快照，
            // 避免监控/诊断端点被连接超时拖到数十秒。
            using var permissionCts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
            permissionCts.CancelAfter(TimeSpan.FromSeconds(3));

            if (!await permissionService.HasPermissionAsync(userId.Value, permissionCode, permissionCts.Token))
            {
                return Results.Json(new ApiResponse(ResultCodes.PermissionDenied, "权限不足"), statusCode: 403);
            }

            return await next(context);
        });

        return builder;
    }
}
