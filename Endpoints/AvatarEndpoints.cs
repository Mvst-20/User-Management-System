using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class AvatarEndpoints
{
    public static void MapAvatarEndpoints(this WebApplication app)
    {
        var avatarsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");

        // 上传/更换头像
        app.MapPost("/api/users/{id}/avatar", async (
            ulong id,
            IFormFile file,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var currentUser = httpContext.User;

            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (!currentUser.IsOwnResource(id) && !currentUser.IsAdmin())
            {
                return Results.StatusCode(403);
            }

            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            // 验证文件类型
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType.ToLower()))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "只支持 JPG、PNG、GIF、WebP 格式的图片"));
            }

            // 验证文件大小（最大 5MB）
            if (file.Length > 5 * 1024 * 1024)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_FileTooLarge, "图片大小不能超过 5MB"));
            }

            // 生成文件名
            var extension = Path.GetExtension(file.FileName);
            var fileName = $"{user.Id}_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(avatarsPath, fileName);

            // 删除旧头像
            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                    user.AvatarUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }

            // 保存新头像
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 更新用户头像路径
            user.AvatarUrl = $"/avatars/{fileName}";
            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.UploadAvatarSuccess, "头像上传成功", 
                new AvatarDataResponse(user.AvatarUrl)));
        }).RequireAuthorization();

        // 删除头像
        app.MapDelete("/api/users/{id}/avatar", async (
            ulong id,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var currentUser = httpContext.User;

            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (!currentUser.IsOwnResource(id) && !currentUser.IsAdmin())
            {
                return Results.StatusCode(403);
            }

            if (user.Status == UserStatus.Banned || user.Status == UserStatus.Deleted)
            {
                return Results.StatusCode(403);
            }

            // 删除头像文件
            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                    user.AvatarUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }

            user.AvatarUrl = null;
            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.DeleteAvatarSuccess, "头像已删除"));
        }).RequireAuthorization();

        // 获取头像（公开）
        app.MapGet("/api/avatars/{filename}", (
            string filename) =>
        {
            var filePath = Path.Combine(avatarsPath, filename);
            
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            var extension = Path.GetExtension(filename).ToLower();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            return Results.File(filePath, contentType);
        });
    }
}
