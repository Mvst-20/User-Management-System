namespace UserManagementSystem.DTOs;

/// <summary>
/// API 响应码定义
/// </summary>
public static class ResultCodes
{
    // ============ 通用成功/失败 (0xx) ============
    public const int Success = 0;                    // 成功
    public const int Error = 1;                      // 通用错误
    public const int ValidationError = 2;            // 参数验证错误
    public const int Unauthorized = 3;              // 未授权
    public const int Forbidden = 4;                  // 禁止访问
    public const int NotFound = 5;                  // 资源不存在
    public const int InternalServerError = 6;       // 服务器内部错误
    public const int TooManyRequests = 7;           // 请求过于频繁

    // ============ 用户注册相关 (1xx) ============
    public const int RegisterSuccess = 100;          // 注册成功
    public const int RegisterFail_UsernameExists = 101;     // 用户名已存在
    public const int RegisterFail_EmailExists = 102;        // 邮箱已被注册
    public const int RegisterFail_PhoneExists = 103;        // 手机号已被使用

    // ============ 登录相关 (2xx) ============
    public const int LoginSuccess = 200;             // 登录成功
    public const int LoginFail_UserNotFound = 201;   // 用户不存在或密码错误
    public const int LoginFail_AccountDeleted = 203; // 账号已注销
    public const int LoginFail_AccountBanned = 204;  // 账号已被封禁

    // ============ 邮箱验证相关 (3xx) ============
    public const int EmailVerifySuccess = 300;       // 邮箱验证成功
    public const int EmailVerifyFail_InvalidToken = 301;    // 无效的验证链接
    public const int EmailVerifyFail_TokenExpired = 302;     // 验证链接已过期
    public const int EmailVerifyFail_InvalidType = 303;      // 无效的验证类型
    public const int EmailVerifyFail_UserNotFound = 304;     // 用户不存在
    public const int ResendVerificationSuccess = 310;      // 验证邮件已发送
    public const int ResendVerificationFail_RateLimited = 311;  // 请求过于频繁
    public const int ResendVerificationFail_UserNotFound = 312; // 用户不存在
    public const int ResendVerificationFail_AlreadyVerified = 313; // 用户已验证

    // ============ 邮箱修改相关 (4xx) ============
    public const int ChangeEmailRequestSuccess = 400;      // 邮箱修改请求成功
    public const int ChangeEmailRequestFail_SameEmail = 401;     // 新邮箱与旧邮箱相同
    public const int ChangeEmailRequestFail_EmailExists = 402;  // 新邮箱已被使用
    public const int ChangeEmailRequestFail_WrongPassword = 403; // 密码错误
    public const int ChangeEmailConfirmSuccess = 410;       // 邮箱修改确认成功
    public const int ChangeEmailConfirmFail_InvalidToken = 411; // 无效的验证链接
    public const int ChangeEmailConfirmFail_TokenExpired = 412;  // 验证链接已过期
    public const int ChangeEmailConfirmFail_NoNewEmail = 413;    // 无效的新邮箱地址

    // ============ 密码修改相关 (5xx) ============
    public const int ChangePasswordSuccess = 500;          // 密码修改成功
    public const int ChangePasswordFail_WrongCurrent = 501;     // 当前密码错误
    public const int ChangePasswordFail_SamePassword = 502;     // 新密码与旧密码相同

    // ============ 用户操作相关 (6xx) ============
    public const int GetUserSuccess = 600;            // 获取用户成功
    public const int GetUserFail_NotFound = 601;      // 用户不存在
    public const int UpdateUserSuccess = 610;          // 更新用户成功
    public const int UpdateUserFail_NotFound = 611;    // 用户不存在
    public const int UpdateUserFail_Forbidden = 612;   // 禁止修改
    public const int UpdateUserFail_UsernameExists = 613;    // 用户名已存在
    public const int UpdateUserFail_EmailExists = 614;      // 邮箱已被使用
    public const int UpdateUserFail_PhoneExists = 615;      // 手机号已被使用
    public const int DeleteUserSuccess = 620;          // 删除用户成功
    public const int DeleteUserFail_NotFound = 621;    // 用户不存在
    public const int GetUsersSuccess = 630;            // 获取用户列表成功

    // ============ 头像相关 (7xx) ============
    public const int UploadAvatarSuccess = 700;        // 上传头像成功
    public const int UploadAvatarFail_InvalidType = 701;    // 不支持的图片格式
    public const int UploadAvatarFail_FileTooLarge = 702;   // 图片大小超过限制
    public const int UploadAvatarFail_Forbidden = 703;     // 禁止上传
    public const int DeleteAvatarSuccess = 710;        // 删除头像成功
    public const int DeleteAvatarFail_NotFound = 711;  // 头像不存在

    // ============ 管理员相关 (8xx) ============
    public const int AdminStatsSuccess = 800;          // 获取统计成功
    public const int ServerStatusSuccess = 810;        // 获取服务器状态成功

    // ============ 邮件发送相关 (9xx) ============
    public const int EmailSendSuccess = 900;           // 邮件发送成功
    public const int EmailSendFail = 901;              // 邮件发送失败
}
