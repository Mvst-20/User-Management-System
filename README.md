# User Management System

基于 ASP.NET Core 8 Minimal API + MySQL 8.0 构建的用户管理系统。

## 功能特性

- **用户认证**：JWT Bearer Token 认证，支持 Token 过期配置
- **密码安全**：BCrypt 哈希存储（Work Factor 12），防止暴力破解
- **邮箱验证**：完整的注册验证和邮箱修改流程，支持频率限制
- **头像管理**：支持上传、下载、删除用户头像（路径遍历防护）
- **在线状态**：基于内存的在线用户跟踪，自动过期清理
- **管理员功能**：用户统计、服务器状态监控、邮件测试
- **后台服务**：自动清理过期 Token 和过期在线状态
- **自动初始化**：首次启动自动创建管理员账户
- **输入验证**：所有请求 DTO 使用 Data Annotations 验证
- **注册限流**：防止批量注册攻击
- **异常处理**：统一异常中间件，生产环境隐藏内部错误信息

## 技术栈

- ASP.NET Core 8 Minimal API
- Entity Framework Core 8 (Pomelo.EntityFrameworkCore.MySql)
- MySQL 8.0
- JWT Bearer Authentication
- MailKit (SMTP 邮件发送)
- BCrypt.Net-Next

## 快速开始

### 1. 配置数据库

编辑 `appsettings.json`：

```json
{
  "ConnectionStrings": {
    "Default": "server=localhost;database=userdb;user=root;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;"
  }
}
```

> 连接字符串支持 MySQL 连接池参数：`MaximumPoolSize`（最大连接数）、`MinimumPoolSize`（最小连接数）、`ConnectionLifeTime`（连接寿命秒数）。

### 2. 配置 SMTP

```json
{
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "your-email@example.com",
    "Password": "your-auth-code",
    "From": "noreply@example.com",
    "FromName": "User Management System"
  }
}
```

> 端口 587 自动使用 STARTTLS，端口 465 自动使用 SSL 直连。

### 3. 配置 JWT

```json
{
  "Jwt": {
    "Key": "YourSuperSecretKeyThatShouldBeAtLeast64CharactersLongForHMACSHA256!!",
    "Issuer": "UserManagementAPI",
    "Audience": "UserManagementClient",
    "ExpiryMinutes": 120
  }
}
```

> **安全警告**：JWT Key 至少 64 字符，建议使用随机生成的十六进制字符串。切勿使用默认值部署到生产环境。

### 4. 配置 CORS

```json
{
  "AppSettings": {
    "AllowedOrigins": "https://example.com,https://app.example.com"
  }
}
```

- `AllowedOrigins` 为空时允许所有来源（仅适合开发环境）
- 多个来源用逗号分隔
- **生产环境务必配置**，禁止使用空值

### 5. 配置 API 基础 URL

```json
{
  "AppSettings": {
    "ApiBaseUrl": "https://api.yourdomain.com"
  }
}
```

- 用于生成验证邮件中的链接
- 开发环境使用 `http://localhost:5000`
- **生产环境必须使用 HTTPS**

### 6. 运行

```bash
# 开发环境
dotnet run

# 生产环境
dotnet run --environment Production
```

首次启动时，系统会自动：
- 创建数据库和表
- 生成初始管理员账户（用户名: `admin`，邮箱: `admin@system.local`）
- 在控制台和 `logs/init.log` 中显示随机生成的密码

## 生产环境部署清单

### 必须完成

- [ ] 修改 JWT Key 为 64+ 字符的随机字符串
- [ ] 修改数据库连接字符串中的密码
- [ ] 修改 SMTP 授权码
- [ ] 配置 `ApiBaseUrl` 为 HTTPS 地址
- [ ] 配置 `AllowedOrigins` 限制跨域来源
- [ ] 配置反向代理（Nginx/Caddy）并启用 HTTPS
- [ ] 登录后立即修改管理员密码

### 建议完成

- [ ] 使用 EF Core Migrations 管理数据库变更（`dotnet ef migrations add` / `dotnet ef database update`）
- [ ] 配置日志输出到文件（推荐使用 Serilog）
- [ ] 配置进程管理工具（systemd / Supervisor / PM2）
- [ ] 设置数据库定时备份
- [ ] 在反向代理层配置速率限制

## API 端点

### 公开端点（无需认证）

| 方法 | 路由 | 描述 | 请求体/参数 |
|------|------|------|-------------|
| POST | `/api/users/register` | 用户注册 | `{username, email, password, extras?}` |
| POST | `/api/users/login` | 用户登录 | `{login, password}` |
| GET | `/api/users/verify-email` | 验证邮箱 | Query: `?token=xxx` |
| POST | `/api/users/resend-verification` | 重新发送验证邮件 | `{email}` |
| GET | `/api/users/verify-email-change` | 确认修改邮箱 | Query: `?token=xxx` |
| GET | `/api/avatars/{filename}` | 获取头像 | - |
| GET | `/health` | 健康检查 | - |

**请求验证规则**：
- `username`：3-64 个字符
- `email`：有效邮箱格式，最大 128 字符
- `password`：8-128 个字符
- `phone`：最大 20 个字符

**注册限流**：同一邮箱每分钟最多 5 次注册请求。

### 用户端点（需要认证）

| 方法 | 路由 | 描述 | 权限 | 请求体 |
|------|------|------|------|--------|
| GET | `/api/users/{id}` | 获取用户信息 | 本人或管理员 | - |
| PUT | `/api/users/{id}` | 完全更新用户 | 本人或管理员 | `{username?, email?, phone?, role?, extras?}` |
| PATCH | `/api/users/{id}` | 部分更新用户 | 本人或管理员 | `{username?, phone?, extras?}` |
| DELETE | `/api/users/{id}` | 软删除用户 | 仅管理员 | - |
| GET | `/api/users` | 分页查询用户列表 | 仅管理员 | Query: `?page=1&pageSize=10&status=&role=&search=` |
| POST | `/api/users/{id}/change-password` | 修改密码 | 本人 | `{currentPassword, newPassword}` |
| POST | `/api/users/{id}/change-email` | 申请修改邮箱 | 本人 | `{newEmail, password}` |
| POST | `/api/users/{id}/avatar` | 上传头像 | 本人或管理员 | `multipart/form-data: file` |
| DELETE | `/api/users/{id}/avatar` | 删除头像 | 本人或管理员 | - |
| POST | `/api/users/logout` | 用户登出 | 已认证用户 | - |

> **邮箱修改**：普通用户不能通过 PUT/PATCH 直接修改邮箱，必须通过 change-email 流程（验证密码 → 发送验证邮件 → 确认修改）。管理员可直接通过 PUT 修改。

> **头像上传**：仅支持 JPG/PNG/GIF/WebP 格式，最大 5MB。

### 管理员端点（需要管理员权限）

| 方法 | 路由 | 描述 | 参数 |
|------|------|------|------|
| GET | `/api/admin/stats/online-users` | 在线用户数量 | - |
| GET | `/api/admin/stats/new-users` | 新注册用户数 | Query: `?days=30` |
| GET | `/api/admin/stats/active-users` | 活跃用户数 | Query: `?days=30` |
| GET | `/api/admin/server/status` | 服务器状态 | - |
| POST | `/api/admin/email/test` | 发送测试邮件 | `{to, subject, body}` |

**统计 API 参数说明**：

- `days`（Query 参数，正整数）：统计天数范围，如 `days=30` 表示从 30 天前到今天的统计数据
- 响应包含统计数量、查询天数、开始日期和结束日期

## API 响应格式

所有 API 响应统一使用以下 JSON 格式：

```json
{
  "code": 0,
  "message": "操作成功",
  "data": null,
  "timestamp": "2026-04-06T00:00:00Z"
}
```

分页响应额外包含分页信息：

```json
{
  "code": 630,
  "message": "获取成功",
  "page": 1,
  "pageSize": 10,
  "totalCount": 100,
  "totalPages": 10,
  "items": [],
  "timestamp": "2026-04-06T00:00:00Z"
}
```

### 响应码说明

| 码段 | 类别 | 主要码值 |
|------|------|---------|
| 0xx | 通用 | 0=成功, 1=错误, 2=验证错误, 3=未授权, 4=禁止, 5=不存在, 6=内部错误, 7=频率限制 |
| 1xx | 注册 | 100=成功, 101=用户名存在, 102=邮箱已注册, 103=手机号已使用 |
| 2xx | 登录 | 200=成功, 201=用户名或密码错误, 203=账号已注销, 204=账号已封禁 |
| 3xx | 邮箱验证 | 300=成功, 301=无效链接, 302=链接过期, 303=无效类型, 310=重发成功, 311=频率限制 |
| 4xx | 邮箱修改 | 400=请求成功, 401=邮箱相同, 402=邮箱已使用, 403=密码错误, 410=确认成功 |
| 5xx | 密码修改 | 500=成功, 501=当前密码错误, 502=新旧密码相同 |
| 6xx | 用户操作 | 600=获取成功, 610=更新成功, 612=禁止修改, 620=删除成功, 630=列表成功 |
| 7xx | 头像 | 700=上传成功, 701=格式错误, 702=文件过大, 710=删除成功 |
| 8xx | 管理员 | 800=统计成功, 810=服务器状态成功 |
| 9xx | 邮件 | 900=发送成功, 901=发送失败 |

## 用户状态

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | Normal | 正常用户，完整功能 |
| 2 | Banned | 被封禁，无法登录 |
| 3 | Unverified | 待验证，已注册但未验证邮箱 |
| 4 | Deleted | 软删除，账号已注销 |

## 用户角色

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | User | 普通用户，基本权限 |
| 2 | Admin | 管理员，管理用户、查看统计等 |

## 项目结构

```
UCS/
├── Configuration/       # 配置类 (AppConfiguration, JwtConfig, SmtpConfig, AppSettings)
├── Data/               # 数据库上下文 (ApplicationDbContext)
├── DTOs/               # 数据传输对象 (ApiResponse, ResultCodes, UserDTOs)
├── Endpoints/          # API 端点定义 (UserEndpoints, AdminEndpoints, AvatarEndpoints)
├── Extensions/         # 扩展方法 (UserExtensions)
├── Middleware/         # 中间件 (ExceptionHandlingMiddleware)
├── Models/             # 实体模型 (User, EmailVerificationToken, 枚举)
├── Services/           # 业务服务
│   ├── ConsoleService.cs          # 控制台输出
│   ├── EmailService.cs            # 邮件发送
│   ├── InitializationService.cs   # 启动初始化
│   ├── JwtService.cs              # JWT Token 生成
│   ├── OnlineUserService.cs       # 在线用户跟踪（单例）
│   ├── TokenCleanupService.cs     # 后台清理服务（每小时）
│   ├── TokenService.cs            # 验证 Token 管理
│   └── UserService.cs             # 用户业务逻辑
├── wwwroot/
│   └── avatars/        # 头像存储
├── logs/               # 日志文件
├── appsettings.json    # 应用配置
├── LICENSE             # Apache License 2.0
└── Program.cs          # 入口点
```

## 安全说明

1. 首次运行后请立即登录并修改管理员密码
2. 初始管理员密码会输出到控制台和 `logs/init.log`
3. **生产环境请务必修改 JWT Key**（至少 64 字符的随机字符串）
4. **生产环境请务必修改数据库密码**
5. 生产环境请配置 `AllowedOrigins` 限制跨域来源
6. 生产环境请配置 `ApiBaseUrl` 为 HTTPS 地址
7. 密码使用 BCrypt Work Factor 12 存储
8. 邮件发送频率限制：同一邮箱 1 分钟内只能请求一次
9. 注册频率限制：同一邮箱每分钟最多 5 次
10. 封禁用户无法登录，也无法获取 JWT Token
11. 头像端点已做路径遍历防护
12. 异常中间件在生产环境隐藏内部错误详情

## 许可证

Apache License 2.0 - 参见 [LICENSE](LICENSE) 文件
