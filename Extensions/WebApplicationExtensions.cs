using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using UserManagementSystem.DTOs;
using UserManagementSystem.Endpoints;
using UserManagementSystem.Middleware;

namespace UserManagementSystem.Extensions;

/// <summary>
/// 应用管道与端点映射：中间件顺序与原实现严格一致。
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 配置中间件管道。顺序：转发头 → 请求日志 → 异常处理 → 目录准备 → Swagger(dev) → 静态文件 → CORS → 认证 → 授权。
    /// </summary>
    public static void UseAppPipeline(this WebApplication app)
    {
        // 反向代理支持：还原真实客户端的 scheme / host / IP。
        // 必须位于管道最前，否则 Request.Scheme 恒为 http，会导致 Cookie 标记、
        // 绝对地址生成与协议判断全部失真（管理前端在 HTTPS 反向代理后尤其明显）。
        // 默认仅信任回环代理（框架默认 KnownNetworks/KnownProxies）；
        // 若代理位于其他主机/容器，需额外登记其地址后再启用。
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000}ms";
            options.GetLevel = (ctx, elapsed, ex) =>
                ex != null ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning
                : LogEventLevel.Information;
        });

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        // 未知前端路由：404 后内部重执行 /not-found，渲染控制台风格的「页面不存在」页。
        // 只处理 GET 且非 /api、非静态资源路径，保证 API 的 404 语义与 access 日志探测不变。
        // 注意：必须位于 UseRouting 之前，内部重执行才会重新匹配 /not-found 端点。
        app.Use(async (context, next) =>
        {
            await next();

            if (context.Response.StatusCode != StatusCodes.Status404NotFound || context.Response.HasStarted)
            {
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method))
            {
                return;
            }

            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") ||
                path.StartsWithSegments("/avatars") ||
                path.StartsWithSegments("/health") ||
                path.StartsWithSegments("/swagger") ||
                path.StartsWithSegments("/_blazor") ||
                path.StartsWithSegments("/_framework") ||
                path.StartsWithSegments("/_content") ||
                (path.Value?.Contains('.') ?? false))
            {
                return;
            }

            context.Request.Path = "/not-found";
            context.SetEndpoint(null);
            await next();
        });

        // 显式 UseRouting：上面的 404 重执行依赖“路由在该中间件之后”，
        // 若使用框架自动插入的 UseRouting（位于管道最前），重执行不会重新匹配端点。
        app.UseRouting();

        EnsureDirectories();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseStaticFiles();

        // Blazor 管理前端需要 antiforgery 中间件。
        // 既有 API 均为 JSON 端点（仅 POST /api/users/{id}/avatar 使用 IFormFile 且已显式 .DisableAntiforgery()），
        // 因此对既有调用方行为无影响。
        app.UseAntiforgery();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
    }

    /// <summary>
    /// 映射全部端点（各业务域）+ 健康检查。
    /// </summary>
    public static void MapAppEndpoints(this WebApplication app)
    {
        // Blazor 管理前端（Interactive Server）
        //
        // AllowAnonymous：管理前端的登录态保存在 SignalR circuit 中（JWT 存于 circuit 内存 +
        // 受保护的 sessionStorage），HTTP 页面请求本身不携带凭证。而 .NET 8 会把页面上的
        // [Authorize] 作为“端点元数据”交给 AuthorizationMiddleware 在 HTTP 层执行，导致
        // 浏览器直接访问页面时必然 401。因此这里放开端点级校验：
        //   · 登录判定改由 circuit 内的 AuthorizeRouteView 执行（它读取页面 [Authorize] 特性，
        //     不依赖端点元数据），未登录会自动跳转登录页；
        //   · 真正的数据与权限边界仍是 REST API —— 无有效 JWT 时一律 401。
        // 页面外壳本身不含任何业务数据，放开不影响安全性。
        app.MapRazorComponents<UserManagementSystem.Components.App>()
            .AddInteractiveServerRenderMode()
            .AllowAnonymous();

        app.MapAuthEndpoints();
        app.MapSessionEndpoints();
        app.MapUserProfileEndpoints();
        app.MapAvatarEndpoints();
        app.MapAdminEndpoints();
        app.MapPermissionEndpoints();
        app.MapUserGroupEndpoints();
        app.MapSystemSettingEndpoints();
        app.MapSmtpConfigEndpoints();
        app.MapSubsystemEndpoints();

        app.MapGet("/health", () => Results.Ok(new ApiResponse(ResultCodes.Success, "healthy",
            new { status = "healthy", timestamp = DateTime.UtcNow })));
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars"));
        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "logs"));
    }
}
