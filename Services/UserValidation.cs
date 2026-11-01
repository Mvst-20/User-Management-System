using System.Net.Mail;
using System.Text;

namespace UserManagementSystem.Services;

/// <summary>
/// 用户输入的统一校验规则。
/// <para>
/// 规则与 README「请求验证规则」以及数据库列宽保持一致：
/// 用户名按 Unicode 字符数 3-64（<c>users.username</c> 为 varchar(64)），
/// 邮箱 ≤128（varchar(128)），密码 8-128。
/// </para>
/// <para>
/// 所有方法在失败时返回面向客户端的中文错误文案，成功时返回 <c>null</c>，
/// 并通过 <c>normalized</c> 输出去除首尾空白后的值。
/// </para>
/// </summary>
public static class UserValidation
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 64;
    public const int EmailMaxLength = 128;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int PhoneMaxLength = 20;

    /// <summary>校验用户名：去首尾空白后 3-64 个 Unicode 字符，且不含控制字符。</summary>
    public static string? ValidateUsername(string? input, out string normalized)
    {
        normalized = (input ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            return "用户名不能为空";
        }

        // 与 MySQL varchar(64) 的字符计数保持一致：emoji 等代理对按 1 个字符计
        var length = normalized.EnumerateRunes().Count();
        if (length is < UsernameMinLength or > UsernameMaxLength)
        {
            return $"用户名长度需为 {UsernameMinLength}-{UsernameMaxLength} 个字符";
        }

        if (normalized.Any(char.IsControl))
        {
            return "用户名不能包含控制字符（如制表符、换行符）";
        }

        return null;
    }

    /// <summary>校验邮箱：≤128 字符、恰好一个 @、无空白、域名含点、可被 MailAddress 解析。</summary>
    public static string? ValidateEmail(string? input, out string normalized)
    {
        normalized = (input ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            return "邮箱不能为空";
        }

        if (normalized.Length > EmailMaxLength)
        {
            return $"邮箱长度不能超过 {EmailMaxLength} 个字符";
        }

        if (normalized.Any(char.IsWhiteSpace))
        {
            return "邮箱格式不正确";
        }

        if (normalized.Count(ch => ch == '@') != 1)
        {
            return "邮箱格式不正确";
        }

        var at = normalized.IndexOf('@');
        var local = normalized[..at];
        var domain = normalized[(at + 1)..];
        if (local.Length == 0 || domain.Length == 0 || !domain.Contains('.'))
        {
            return "邮箱格式不正确";
        }

        if (!MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return "邮箱格式不正确";
        }

        return null;
    }

    /// <summary>校验密码：8-128 个字符，且不能全部为空白（不做 Trim，保持用户输入的原始密码）。</summary>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "密码不能为空";
        }

        if (password.Length is < PasswordMinLength or > PasswordMaxLength)
        {
            return $"密码长度需为 {PasswordMinLength}-{PasswordMaxLength} 个字符";
        }

        if (password.All(char.IsWhiteSpace))
        {
            return "密码不能全部为空白字符";
        }

        return null;
    }

    /// <summary>校验手机号：可空；非空时 ≤20 字符，仅允许数字、+、- 与空格。</summary>
    public static string? ValidatePhone(string? input, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > PhoneMaxLength)
        {
            return $"手机号长度不能超过 {PhoneMaxLength} 个字符";
        }

        if (normalized.Any(ch => !(char.IsDigit(ch) || ch is '+' or '-' or ' ')))
        {
            return "手机号格式不正确";
        }

        return null;
    }
}
