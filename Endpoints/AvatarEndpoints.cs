using ImageMagick;
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
    /// 压缩触发阈值：512KB
    /// </summary>
    private const long CompressionThreshold = 512 * 1024;

    /// <summary>
    /// 缩略图最大边长（像素）
    /// </summary>
    private const int ThumbnailMaxSize = 256;

    /// <summary>
    /// 压缩后 JPEG 质量（1-100）
    /// </summary>
    private const int JpegQuality = 80;

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

    /// <summary>
    /// 压缩图片并生成缩略图。返回 (压缩后的原图字节, 缩略图字节)。
    /// 若图片小于等于阈值，返回 (null, null) 表示无需压缩。
    /// </summary>
    private static (byte[]? compressed, byte[]? thumbnail) CompressImage(Stream sourceStream, string extension, long originalSize)
    {
        if (originalSize <= CompressionThreshold)
            return (null, null);

        sourceStream.Position = 0;

        using var images = new MagickImageCollection();
        images.Read(sourceStream);

        if (images.Count == 0)
            throw new InvalidDataException("图片内容为空或无法解析");

        var format = GetMagickFormat(extension);

        // 压缩原图：保持原始尺寸，降低质量
        ApplyQuality(images, format);
        using var compressedStream = new MemoryStream();
        images.Write(compressedStream, format);
        var compressedBytes = compressedStream.ToArray();

        // 如果压缩后反而更大，放弃压缩
        if (compressedBytes.Length >= originalSize)
            return (null, null);

        // 生成缩略图：复制并展开动画帧后等比缩放，最大边长 256px
        using var thumbnailImages = new MagickImageCollection();
        foreach (var frame in images)
        {
            thumbnailImages.Add(frame.Clone());
        }

        thumbnailImages.Coalesce();
        foreach (var frame in thumbnailImages)
        {
            if (frame.Width > ThumbnailMaxSize || frame.Height > ThumbnailMaxSize)
            {
                frame.Resize(new MagickGeometry(ThumbnailMaxSize, ThumbnailMaxSize));
            }
        }

        ApplyQuality(thumbnailImages, format);
        thumbnailImages.Optimize();

        using var thumbStream = new MemoryStream();
        thumbnailImages.Write(thumbStream, format);
        var thumbBytes = thumbStream.ToArray();

        return (compressedBytes, thumbBytes);
    }

    /// <summary>
    /// 根据扩展名获取保存格式
    /// </summary>
    private static MagickFormat GetMagickFormat(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".gif" => MagickFormat.Gif,
        ".webp" => MagickFormat.WebP,
        _ => MagickFormat.Jpeg
    };

    /// <summary>
    /// 为有损格式设置压缩质量
    /// </summary>
    private static void ApplyQuality(MagickImageCollection images, MagickFormat format)
    {
        if (format is not (MagickFormat.Jpeg or MagickFormat.WebP))
            return;

        foreach (var frame in images)
        {
            frame.Quality = JpegQuality;
        }
    }

    /// <summary>
    /// 删除头像文件（原图 + 缩略图）
    /// </summary>
    private static void DeleteAvatarFiles(string avatarsPath, string? avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl))
            return;

        var fileName = Path.GetFileName(avatarUrl);
        var filePath = Path.Combine(avatarsPath, fileName);
        if (File.Exists(filePath))
            File.Delete(filePath);

        // 删除缩略图
        var thumbPath = GetThumbnailPath(avatarsPath, fileName);
        if (File.Exists(thumbPath))
            File.Delete(thumbPath);
    }

    /// <summary>
    /// 根据原图文件名获取缩略图路径
    /// </summary>
    private static string GetThumbnailPath(string avatarsPath, string originalFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        return Path.Combine(avatarsPath, $"{nameWithoutExt}_thumb{ext}");
    }

    public static void MapAvatarEndpoints(this WebApplication app)
    {
        var avatarsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");

        // 上传/更换头像（允许本人或拥有 avatar:upload 权限）
        app.MapPost("/api/users/{id}/avatar", async (
            ulong id,
            IFormFile? file,
            [FromServices] IUserService userService,
            [FromServices] IPermissionService permissionService,
            [FromServices] IUserGroupService userGroupService,
            HttpContext httpContext) =>
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("AvatarUpload");

            // === 诊断：记录请求基本信息 ===
            var reqContentType = httpContext.Request.ContentType ?? "(null)";
            logger.LogInformation("[AVATAR-UPLOAD] START id={UserId}, fileParam={HasFile}, fileName={FileName}, fileSize={FileSize}, contentType={ContentType}",
                id,
                file != null,
                file?.FileName ?? "(null)",
                file?.Length ?? -1,
                reqContentType);

            // 空文件检查
            if (file == null || file.Length == 0)
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: file is null or empty");
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "请选择要上传的图片"));
            }

            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: user {UserId} not found or deleted", id);
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned)
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: user {UserId} is banned", id);
                return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
            }

            // 授权：本人可操作自己的头像；操作他人需要 user:update（管理员能力）且目标级别低于自己。
            // 仅持有全局 avatar:upload 的普通用户不能覆盖他人头像。
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            if (actorId.Value != id)
            {
                if (!await permissionService.HasPermissionAsync(actorId.Value, PermissionCodes.UserUpdate))
                {
                    logger.LogWarning("[AVATAR-UPLOAD] FAIL: user {ActorId} has no user:update to modify user {UserId}", actorId.Value, id);
                    return Results.Json(new ApiResponse(ResultCodes.PermissionDenied, "权限不足"), statusCode: 403);
                }

                var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
                if (privilegeResult is not null)
                {
                    return privilegeResult;
                }
            }

            // 验证文件扩展名（防止 XSS：拒绝 .html/.svg 等）
            // FileName 安全处理：浏览器发来的文件名通常不含路径，但做防御性处理
            var safeFileName = Path.GetFileName((file.FileName ?? string.Empty).Trim());
            var extension = string.IsNullOrEmpty(safeFileName)
                ? string.Empty
                : Path.GetExtension(safeFileName).ToLowerInvariant();

            logger.LogInformation("[AVATAR-UPLOAD] STEP1: extension='{Ext}', originalFileName='{OrigName}'",
                extension, file.FileName);

            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: invalid extension '{Ext}'", extension);
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType,
                    string.IsNullOrEmpty(extension) ? "文件缺少扩展名" : "不支持的文件扩展名"));
            }

            // 验证文件大小（最大 6MB）
            if (file.Length > 6 * 1024 * 1024)
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: file too large {Size} bytes", file.Length);
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_FileTooLarge, "图片大小不能超过 6MB"));
            }

            // 验证文件内容（magic bytes），防止 Content-Type 伪装
            using var fileStream = file.OpenReadStream();
            var isValidSignature = ValidateFileSignature(fileStream, extension);
            logger.LogInformation("[AVATAR-UPLOAD] STEP2: magicBytes valid={IsValid}, streamLen={Len}",
                isValidSignature, fileStream.Length);

            if (!isValidSignature)
            {
                logger.LogWarning("[AVATAR-UPLOAD] FAIL: magic bytes mismatch for extension '{Ext}'", extension);
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "文件内容与声明的图片格式不匹配"));
            }

            // 生成文件名
            var baseName = $"{user.Id}_{Guid.NewGuid():N}";
            var fileName = $"{baseName}{extension}";
            var filePath = Path.Combine(avatarsPath, fileName);
            string? oldAvatarUrl = user.AvatarUrl;

            logger.LogInformation("[AVATAR-UPLOAD] STEP3: saving to '{Path}', oldAvatar={OldAvatar}",
                filePath, oldAvatarUrl ?? "(null)");

            try
            {
                // 压缩处理（包含在 try 内，捕获图片解码异常）
                fileStream.Position = 0;
                var (compressed, thumbnail) = CompressImage(fileStream, extension, file.Length);

                bool hasThumbnail = false;

                if (compressed != null && thumbnail != null)
                {
                    logger.LogInformation("[AVATAR-UPLOAD] STEP4: compressed (orig={Orig}B → compressed={Comp}B + thumb={Thumb}B)",
                        file.Length, compressed.Length, thumbnail.Length);
                    await File.WriteAllBytesAsync(filePath, compressed);
                    var thumbPath = GetThumbnailPath(avatarsPath, fileName);
                    await File.WriteAllBytesAsync(thumbPath, thumbnail);
                    hasThumbnail = true;
                }
                else
                {
                    logger.LogInformation("[AVATAR-UPLOAD] STEP4: no compression needed, copying raw ({Size}B)", file.Length);
                    fileStream.Position = 0;
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await fileStream.CopyToAsync(stream);
                }

                // DB 更新成功后再清理旧文件
                user.AvatarUrl = $"/avatars/{fileName}";
                await userService.UpdateUserAsync(user);
                logger.LogInformation("[AVATAR-UPLOAD] STEP5: DB updated, new avatarUrl='{Url}'", user.AvatarUrl);
                DeleteAvatarFiles(avatarsPath, oldAvatarUrl);

                logger.LogInformation("[AVATAR-UPLOAD] SUCCESS: userId={UserId}, avatarUrl='{Url}', hasThumb={Thumb}",
                    id, user.AvatarUrl, hasThumbnail);

                return Results.Ok(new ApiResponse(ResultCodes.UploadAvatarSuccess, "头像上传成功",
                    new AvatarUploadDataResponse(user.AvatarUrl, hasThumbnail)));
            }
            catch (Exception ex) when (ex is MagickException or InvalidDataException)
            {
                logger.LogWarning(ex, "[AVATAR-UPLOAD] FAIL: invalid image content");
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "图片内容无效或已损坏"));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[AVATAR-UPLOAD] FAIL: InvalidOperationException — {Message}", ex.Message);
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType,
                    $"图片处理失败：{ex.Message}"));
            }
            catch (Exception ex)
            {
                // 清理已写入的文件后重抛，由全局异常中间件统一返回 500
                logger.LogError(ex, "[AVATAR-UPLOAD] FAIL: unhandled exception while processing");
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                throw;
            }
        }).RequireAuthorization().RequirePermission(PermissionCodes.AvatarUpload, allowSelf: true).DisableAntiforgery();

        // 删除头像（允许本人或拥有 avatar:delete 权限）
        app.MapDelete("/api/users/{id}/avatar", async (
            ulong id,
            [FromServices] IUserService userService,
            [FromServices] IPermissionService permissionService,
            [FromServices] IUserGroupService userGroupService,
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

            // 与上传一致：本人可删除自己的头像；删除他人需要 user:update 且目标级别低于自己
            var actorId = httpContext.User.GetUserId();
            if (actorId is null)
            {
                return Results.Json(new ApiResponse(ResultCodes.Unauthorized, "未授权"), statusCode: 401);
            }

            if (actorId.Value != id)
            {
                if (!await permissionService.HasPermissionAsync(actorId.Value, PermissionCodes.UserUpdate))
                {
                    return Results.Json(new ApiResponse(ResultCodes.PermissionDenied, "权限不足"), statusCode: 403);
                }

                var privilegeResult = await httpContext.ValidatePrivilegeLevelAsync(userGroupService, id);
                if (privilegeResult is not null)
                {
                    return privilegeResult;
                }
            }

            // 删除头像文件（原图 + 缩略图）
            DeleteAvatarFiles(avatarsPath, user.AvatarUrl);

            user.AvatarUrl = null;
            await userService.UpdateUserAsync(user);

            return Results.Ok(new ApiResponse(ResultCodes.DeleteAvatarSuccess, "头像已删除"));
        }).RequireAuthorization().RequirePermission(PermissionCodes.AvatarDelete, allowSelf: true);

        // 获取头像（公开）— 支持 type=thumb 获取缩略图
        app.MapGet("/api/avatars/{filename}", (
            string filename,
            [FromQuery] string? type) =>
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

            // 确定要返回的文件
            var isThumb = string.Equals(type, "thumb", StringComparison.OrdinalIgnoreCase);
            var targetFileName = isThumb
                ? $"{Path.GetFileNameWithoutExtension(filename)}_thumb{Path.GetExtension(filename)}"
                : filename;

            var filePath = Path.Combine(avatarsPath, targetFileName);

            // 如果请求缩略图但不存在，回退到原图
            if (!File.Exists(filePath))
            {
                if (isThumb)
                {
                    filePath = Path.Combine(avatarsPath, filename);
                    if (!File.Exists(filePath))
                        return Results.NotFound();
                }
                else
                {
                    return Results.NotFound();
                }
            }

            // 确保解析后的路径仍在 avatars 目录内
            var fullPath = Path.GetFullPath(filePath);
            var fullAvatarsPath = Path.GetFullPath(avatarsPath);
            if (!fullPath.StartsWith(fullAvatarsPath, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest();
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
