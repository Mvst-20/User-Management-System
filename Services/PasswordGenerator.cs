using System.Security.Cryptography;

namespace UserManagementSystem.Services;

/// <summary>
/// 随机密码生成器（密码学安全）。
/// 用于系统初始化账户密码与管理员重置用户密码。
/// </summary>
public static class PasswordGenerator
{
    /// <summary>字符集排除易混淆字符（0/O、1/l/I 等）。</summary>
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%";

    /// <summary>生成长度受限在 8-128 之间的随机密码（与接口密码策略一致）。</summary>
    public static string Generate(int length)
    {
        length = Math.Clamp(length, 8, 128);

        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = AllowedChars[RandomNumberGenerator.GetInt32(AllowedChars.Length)];
        }

        return new string(result);
    }
}
