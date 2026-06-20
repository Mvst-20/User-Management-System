using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
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

        using var image = Image.Load(sourceStream);

        // 压缩原图：保持原始尺寸，降低质量
        using var compressedStream = new MemoryStream();
        SaveImage(image, compressedStream, extension);
        var compressedBytes = compressedStream.ToArray();

        // 如果压缩后反而更大，放弃压缩
        if (compressedBytes.Length >= originalSize)
            return (null, null);

        // 生成缩略图：等比缩放，最大边长 256px
        using var thumbImage = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(ThumbnailMaxSize, ThumbnailMaxSize)
        }));

        using var thumbStream = new MemoryStream();
        SaveImage(thumbImage, thumbStream, extension);
        var thumbBytes = thumbStream.ToArray();

        return (compressedBytes, thumbBytes);
    }

    /// <summary>
    /// 根据扩展名保存图片到流
    /// </summary>
    private static void SaveImage(Image image, Stream stream, string extension)
    {
        switch (extension.ToLower())
        {
            case ".jpg":
            case ".jpeg":
                image.SaveAsJpeg(stream, new JpegEncoder { Quality = JpegQuality });
                break;
            case ".png":
                image.SaveAsPng(stream, new PngEncoder { CompressionLevel = PngCompressionLevel.DefaultCompression });
                break;
            case ".gif":
                image.SaveAsGif(stream);
                break;
            case ".webp":
                image.SaveAsWebp(stream, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = JpegQuality });
                break;
            default:
                image.SaveAsJpeg(stream, new JpegEncoder { Quality = JpegQuality });
                break;
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
            HttpContext httpContext) =>
        {
            // 空文件检查
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "请选择要上传的图片"));
            }

            var user = await userService.GetByIdAsync(id);
            if (user == null || user.Status == UserStatus.Deleted)
            {
                return Results.NotFound(new ApiResponse(ResultCodes.UpdateUserFail_NotFound, "用户不存在"));
            }

            if (user.Status == UserStatus.Banned)
            {
                return Results.Json(new ApiResponse(ResultCodes.AccountBanned, "账号已被封禁"), statusCode: 403);
            }

            // 验证文件类型（宽松匹配：支持 image/jpg 非标类型、忽略 charset 后缀）
            var mime = file.ContentType.ToLower().Split(';')[0].Trim();
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/jpg" };
            if (!allowedTypes.Contains(mime))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "只支持 JPG、PNG、GIF、WebP 格式的图片"));
            }

            // 验证文件扩展名（防止 XSS：拒绝 .html/.svg 等）
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!AllowedExtensions.Contains(extension))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "不支持的文件扩展名"));
            }

            // 验证文件大小（最大 6MB）
            if (file.Length > 6 * 1024 * 1024)
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_FileTooLarge, "图片大小不能超过 6MB"));
            }

            // 验证文件内容（magic bytes），防止 Content-Type 伪装
            using var fileStream = file.OpenReadStream();
            if (!ValidateFileSignature(fileStream, extension))
            {
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "文件内容与声明的图片格式不匹配"));
            }

            // 生成文件名
            var baseName = $"{user.Id}_{Guid.NewGuid():N}";
            var fileName = $"{baseName}{extension}";
            var filePath = Path.Combine(avatarsPath, fileName);
            string? oldAvatarUrl = user.AvatarUrl;

            try
            {
                // 压缩处理（包含在 try 内，捕获图片解码异常）
                fileStream.Position = 0;
                var (compressed, thumbnail) = CompressImage(fileStream, extension, file.Length);

                bool hasThumbnail = false;

                if (compressed != null && thumbnail != null)
                {
                    await File.WriteAllBytesAsync(filePath, compressed);
                    var thumbPath = GetThumbnailPath(avatarsPath, fileName);
                    await File.WriteAllBytesAsync(thumbPath, thumbnail);
                    hasThumbnail = true;
                }
                else
                {
                    fileStream.Position = 0;
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await fileStream.CopyToAsync(stream);
                }

                // DB 更新成功后再清理旧文件
                user.AvatarUrl = $"/avatars/{fileName}";
                await userService.UpdateUserAsync(user);
                DeleteAvatarFiles(avatarsPath, oldAvatarUrl);

                return Results.Ok(new ApiResponse(ResultCodes.UploadAvatarSuccess, "头像上传成功",
                    new AvatarUploadDataResponse(user.AvatarUrl, hasThumbnail)));
            }
            catch (UnknownImageFormatException)
            {
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "不支持的图片格式或图片已损坏"));
            }
            catch (InvalidImageContentException)
            {
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                return Results.BadRequest(new ApiResponse(ResultCodes.UploadAvatarFail_InvalidType, "图片内容无效或已损坏"));
            }
            catch
            {
                DeleteAvatarFiles(avatarsPath, $"/avatars/{fileName}");
                throw;
            }
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
