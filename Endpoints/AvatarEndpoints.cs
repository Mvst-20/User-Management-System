using Microsoft.AspNetCore.Mvc;
using UserManagementSystem.DTOs;
using UserManagementSystem.Extensions;
using UserManagementSystem.Models;
using UserManagementSystem.Services;

namespace UserManagementSystem.Endpoints;

public static class AvatarEndpoints
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    /// <summary>
    /// 图片文件魔数（magic bytes）签名，用于验证文件真实类型
    /// </summary>
    private static readonly Dictionary<string, byte[]> ImageSignatures = new()
    {
        { ".jpg", new byte[] { 0xFF, 0xD8, 0xFF } },
        { ".jpeg", new byte[] { 0xFF, 0xD8, 0xFF } },
        { ".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
        { ".gif", new byte[] { 0x47, 0x49, 0x46, 0x38 } }, // GIF8
        { ".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 } }, // RIFF (WebP container)
    };

    /// <summary>
    /// 验证文件流的前几个字节是否匹配指定扩展名的魔数签名
    /// </summary>
    private static bool ValidateFileSignature(Stream fileStream, string extension)
    {
        if (!ImageSignatures.TryGetValue(extension, out var signature))
            return false;

        var buffer = new byte[signature.Length];
        var originalPosition = fileStream.Position;
        fileStream.Position = 0;

        try
        {
            var bytesRead = fileStream.Read(buffer, 0, signature.Length);
            if (bytesRead < signature.Length)
                return false;

            for (int i = 0; i < signature.Length; i++)
            {
                if (buffer[i] != signature[i])
                    return false;
            }
            return true;
        }
        finally
        {
            fileStream.Position = originalPosition;
        }
    }

    public static void MapAvatarEndpoints(this WebApplication app)
    {
        var avatarsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");

        // 上传/更换头像（允许本人或拥有 avatar:upload 权限）
        app.MapPost("/api/users/{id}/avatar", async (
            ulong id,
            IFormFile file,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
            }

            // 验证文件类型
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType.ToLower()))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "只支持 JPG、PNG、GIF、WebP 格式的图片"));
            }

            // 验证文件扩展名（防止 XSS：拒绝 .html/.svg 等）
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!AllowedExtensions.Contains(extension))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "不支持的文件扩展名"));
            }

            // 验证文件大小（最大 5MB）
            if (file.Length > 5 * 1024 * 1024)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_FileTooLarge, "图片大小不能超过 5MB"));
            }

            // 验证文件内容（magic bytes），防止 Content-Type 伪装
            using var fileStream = file.OpenReadStream();
            if (!ValidateFileSignature(fileStream, extension))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "文件内容与声明的图片格式不匹配"));
            }

            // 生成文件名（使用固定安全扩展名，不使用用户提供的扩展名）
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

            // 保存新头像（使用已打开的 fileStream）
            fileStream.Position = 0;
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(stream);
            }

            // 更新用户头像路径
            user.AvatarUrl = $"/avatars/{fileName}";
            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.UploadAvatarSuccess, "头像上传成功",
                new AvatarDataResponse(user.AvatarUrl)));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AvatarUpload, allowSelf: true);

        // 删除头像（允许本人或拥有 avatar:delete 权限）
        app.MapDelete("/api/users/{id}/avatar", async (
            ulong id,
            [FromServices] IUserService userService,
            HttpContext httpContext) =>
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
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
        }).RequireAuthorization().RequirePermission(PermissionCodes.AvatarDelete, allowSelf: true);

        // 获取头像（公开）— 添加路径遍历防护
        app.MapGet("/api/avatars/{filename}", (
            string filename) =>
        {
            // 防止路径遍历攻击
            if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\') || filename.Contains(Path.DirectorySeparatorChar))
            {
                return Results.BadRequest();
            }

            var extension = Path.GetExtension(filename).ToLower();
            if (!AllowedExtensions.Contains(extension))
            {
                return Results.BadRequest();
            }

            var filePath = Path.Combine(avatarsPath, filename);

            // 确保解析后的路径仍在 avatars 目录内
            var fullPath = Path.GetFullPath(filePath);
            var fullAvatarsPath = Path.GetFullPath(avatarsPath);
            if (!fullPath.StartsWith(fullAvatarsPath, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest();
            }

            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

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
