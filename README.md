# User Management System

基于 ASP.NET Core 8 Minimal API + MySQL 8.0 构建的用户管理系统。

## 功能特性

- **用户认证**：JWT Bearer Token 认证，支持 Token 过期配置
- **密码安全**：BCrypt 哈希存储（Work Factor 12），防止暴力破解
- **邮箱验证**：完整的注册验证和邮箱修改流程，支持频率限制
- **头像管理**：支持上传、下载、删除用户头像（路径遍历防护 + 图片压缩 + 缩略图）
- **在线状态**：基于内存的在线用户跟踪，自动过期清理
- **权限体系**：基于用户组的细粒度权限控制，支持 A/B/C 特殊权限
- **用户组管理**：支持创建、编辑、删除用户组及分配权限和成员
- **后台服务**：自动清理过期 Token 和过期在线状态
- **自动初始化**：首次启动自动创建系统用户、管理员账户、权限和用户组
- **输入验证**：所有请求 DTO 使用 Data Annotations 验证
- **注册限流**：防止批量注册攻击
- **登录防护**：基于 IP + 账号的双重暴力破解防护（IP 20次/5分钟，账号 5次/5分钟）
- **Token 撤销**：JWT Token 黑名单机制，登出和密码修改时自动撤销当前 Token
- **头像验证**：上传时验证文件魔数签名（magic bytes），防止 Content-Type 伪装；大于 512KB 自动压缩并生成缩略图
- **密码安全策略**：密码修改后强制下线并撤销当前 Token，需重新登录
- **异常处理**：统一异常中间件，生产环境隐藏内部错误信息

## 技术栈

- ASP.NET Core 8 Minimal API
- Entity Framework Core 8 (Pomelo.EntityFrameworkCore.MySql)
- MySQL 8.0
- JWT Bearer Authentication
- MailKit (SMTP 邮件发送)
- BCrypt.Net-Next
- SixLabors.ImageSharp

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

> 端口 587 自动使用 STARTTLS，端口 465 自动使用 SSL 直连，其他端口默认使用 STARTTLS（可通过 `UseSsl: false` 禁用）。

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

- `AllowedOrigins` 为空时：开发环境允许所有来源，**生产环境阻止所有跨域请求**
- 多个来源用逗号分隔
- **生产环境务必配置**，否则跨域请求将被阻止

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

### 6. 配置应用参数

```json
{
  "AppSettings": {
    "TokenExpiryMinutes": 20,
    "AdminPasswordLength": 12
  }
}
```

- `TokenExpiryMinutes`：邮箱验证 Token 的有效期（分钟），默认 20 分钟
- `AdminPasswordLength`：首次启动时自动生成的管理员和系统用户密码长度，默认 12 位

### 7. 运行

```bash
# 开发环境
dotnet run

# 生产环境
dotnet run --environment Production
```

首次启动时，系统会自动：
- 创建数据库和表
- 初始化权限表（含 A/B/C 特殊权限和各 API 权限）
- 初始化用户组（初始用户、超级管理员、管理员、普通用户）
- 创建系统初始用户（uid=0，用户名: `system`，持有 A 权限）
- 生成超级管理员账户（用户名: `admin`，邮箱: `admin@system.local`，属于"超级管理员"组）
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
- [ ] 妥善保管系统初始用户（uid=0）密码

## 权限体系

### 概述

系统采用**基于用户组的权限控制**模型：每个 API 端点对应一个权限代码，用户通过其所属的用户组获取权限。调用 API 时，系统检查用户所属用户组是否拥有该 API 对应的权限。

> 用户可同时属于多个用户组，最终权限为所有组权限的并集。系统初始用户（uid=0）始终保留在"初始用户"组中，不可移除。

### 特殊权限

| 权限 | 代码 | 说明 |
|------|------|------|
| A 权限 | `PERM_A` | 最高权限，可使用全站所有 API（包括 B、C 权限覆盖的 API）。仅"初始用户"组持有，不可分配给其他用户组 |
| B 权限 | `PERM_B` | 权限组管理权限，可管理权限表和用户组（除 ABC 外的所有权限） |
| C 权限 | `PERM_C` | 服务器配置管理权限，可使用服务器管理相关 API |

### 预设用户组

| 用户组 | 说明 | 成员锁定 | 可删除 | 持有权限 |
|--------|------|----------|--------|---------|
| 初始用户 | 仅 uid=0 成员，持有 A 权限 | 是（不可添加/移除成员） | 否 | PERM_A |
| 超级管理员 | 除 A 外的所有权限 | 否 | 否 | PERM_B, PERM_C + 全部 API 权限 |
| 管理员 | 用户管理和基本管理权限 | 否 | 是 | 用户管理 + 统计 + 头像 + 用户组查看 + 登出 |
| 普通用户 | 基本用户权限 | 否 | 是 | 查看用户 + 更新自己 + 修改密码/邮箱 + 头像 + 登出 |

> **初始用户组**：不可编辑（名称、描述、权限均不可修改）、不可删除、不可添加或移除成员。
>
> **超级管理员组**：不可删除，但可修改成员和权限。

### 权限代码列表

| 分类 | 代码 | 名称 | 说明 |
|------|------|------|------|
| special | `PERM_A` | 最高权限 | 全站所有 API |
| special | `PERM_B` | 权限管理权限 | 管理权限表和用户组（除 ABC） |
| special | `PERM_C` | 服务器管理权限 | 服务器管理 API |
| user | `user:read` | 查看用户 | 查看用户信息 |
| user | `user:update` | 更新用户 | 更新用户信息 |
| user | `user:delete` | 删除用户 | 删除用户 |
| user | `user:list` | 用户列表 | 查看用户列表 |
| user | `user:change-password` | 修改密码 | 修改用户密码 |
| user | `user:change-email` | 修改邮箱 | 修改用户邮箱 |
| user | `user:logout` | 登出 | 用户登出 |
| avatar | `avatar:upload` | 上传头像 | 上传用户头像 |
| avatar | `avatar:delete` | 删除头像 | 删除用户头像 |
| admin | `admin:stats` | 查看统计 | 管理员统计数据（C 权限覆盖） |
| admin | `admin:server` | 服务器管理 | 服务器状态和配置（C 权限覆盖） |
| permission | `permission:read` | 查看权限 | 查看权限列表（B 权限覆盖） |
| permission | `permission:create` | 创建权限 | 创建新权限（B 权限覆盖） |
| permission | `permission:update` | 更新权限 | 更新权限信息（B 权限覆盖） |
| permission | `permission:delete` | 删除权限 | 删除权限（B 权限覆盖） |
| user-group | `user-group:read` | 查看用户组 | 查看用户组（B 权限覆盖） |
| user-group | `user-group:create` | 创建用户组 | 创建新用户组（B 权限覆盖） |
| user-group | `user-group:update` | 更新用户组 | 更新用户组信息（B 权限覆盖） |
| user-group | `user-group:delete` | 删除用户组 | 删除用户组（B 权限覆盖） |
| user-group | `user-group:manage-members` | 管理成员 | 添加/移除用户组成员（B 权限覆盖） |
| user-group | `user-group:manage-permissions` | 管理权限 | 设置用户组权限（B 权限覆盖） |

### 权限检查逻辑

1. 用户请求 API → 从 JWT 获取用户 ID
2. 查询用户所属的所有用户组及其权限（带内存缓存，5 分钟过期）
3. 检查顺序：
   - 拥有 **A 权限** → 直接通过
   - API 属于 B 权限覆盖范围（权限管理 + 用户组管理）且 拥有 **B 权限** → 通过
   - API 属于 C 权限覆盖范围 且 拥有 **C 权限** → 通过
   - 拥有该 API 对应的**具体权限** → 通过
   - 否则 → 403 Forbidden
4. 部分 API 支持 `allowSelf`：访问自己的资源时跳过权限检查

## API 端点

### 公开端点（无需认证和权限）

| 方法 | 路由 | 描述 | 请求体/参数 |
|------|------|------|-------------|
| POST | `/api/users/register` | 用户注册 | `{username, email, password, extras?}` |
| POST | `/api/users/login` | 用户登录 | `{login, password}` |
| POST | `/api/users/refresh` | 刷新 Token（免登录） | `{refreshToken}` |
| GET | `/api/users/verify-email` | 验证邮箱 | Query: `?token=xxx` |
| POST | `/api/users/resend-verification` | 重新发送验证邮件 | `{email}` |
| GET | `/api/users/verify-email-change` | 确认修改邮箱 | Query: `?token=xxx` |
| GET | `/api/avatars/{filename}` | 获取头像（支持 `?type=thumb` 获取缩略图） | - |
| GET | `/api/permissions/custom` | 获取自定义权限列表 | - |
| GET | `/health` | 健康检查 | - |

**请求验证规则**：
- `username`：3-64 个字符
- `email`：有效邮箱格式，最大 128 字符
- `password`：8-128 个字符

**注册限流**：同一邮箱每分钟最多 5 次注册请求。

> 新注册用户会自动加入"普通用户"组。

### 用户端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | allowSelf |
|------|------|------|---------|-----------|
| GET | `/api/users/{id}` | 获取用户信息 | `user:read` | 是 |
| GET | `/api/users/{id}/groups` | 获取用户所属用户组 | `user:read` | 是 |
| PUT | `/api/users/{id}/groups` | 批量设置用户所属用户组 | `user-group:manage-members` | 否 |
| PUT | `/api/users/{id}` | 完全更新用户 | `user:update` | 是 |
| PATCH | `/api/users/{id}` | 部分更新用户 | `user:update` | 是 |
| DELETE | `/api/users/{id}` | 软删除用户 | `user:delete` | 否 |
| GET | `/api/users` | 分页查询用户列表 | `user:list` | - |
| POST | `/api/users/{id}/change-password` | 修改密码 | `user:change-password` | 否（仅限本人） |
| POST | `/api/users/{id}/change-email` | 申请修改邮箱 | `user:change-email` | 否（仅限本人） |
| POST | `/api/users/{id}/avatar` | 上传头像 | `avatar:upload` | 是 |
| DELETE | `/api/users/{id}/avatar` | 删除头像 | `avatar:delete` | 是 |
| POST | `/api/users/logout` | 用户登出 | `user:logout` | 否 |

> **allowSelf**：访问自己的资源（路由参数 `{id}` 与当前用户 ID 匹配）时跳过权限检查。change-password 和 change-email 不使用 allowSelf，而是始终要求权限并手动校验仅限本人操作。

> **邮箱修改**：普通用户不能通过 PUT/PATCH 直接修改邮箱，必须通过 change-email 流程（验证密码 → 发送验证邮件 → 确认修改）。管理员可直接通过 PUT 修改。

> **手机号**：仅在 PUT/PATCH 更新时支持，最大 20 字符。注册时暂不支持。

> **头像上传**：仅支持 JPG/PNG/GIF/WebP 格式，最大 6MB。文件大于 512KB 时自动压缩并生成缩略图（256px），小于等于 512KB 时不压缩（缩略图 = 原图）。获取头像时可通过 `?type=thumb` 参数获取缩略图。

### 管理员端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | 特殊覆盖 |
|------|------|------|---------|---------|
| GET | `/api/admin/stats/total-users` | 总用户数量（排除已删除） | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/online-users` | 在线用户数量 | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/new-users` | 新注册用户数 | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/active-users` | 活跃用户数 | `admin:stats` | C 权限 |
| GET | `/api/admin/server/status` | 服务器状态 | `admin:server` | C 权限 |
| POST | `/api/admin/email/test` | 发送测试邮件 | `admin:server` | C 权限 |

**统计 API 参数说明**：

- `days`（Query 参数，正整数）：统计天数范围，如 `days=30` 表示从 30 天前到今天的统计数据

### 权限管理端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | 特殊覆盖 |
|------|------|------|---------|---------|
| GET | `/api/permissions` | 获取所有权限 | `permission:read` | B 权限 |
| POST | `/api/permissions/check` | 权限检查 | 认证即可（查他人需 `permission:read`） |
| POST | `/api/permissions` | 创建权限 | `permission:create` | B 权限 |
| PUT | `/api/permissions/{id}` | 更新权限 | `permission:update` | B 权限 |
| DELETE | `/api/permissions/{id}` | 删除权限 | `permission:delete` | B 权限 |

> B 权限持有者可管理权限表和用户组，但**不可**创建、修改或删除 A/B/C 特殊权限。

### 用户组管理端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/user-groups` | 用户组列表（分页） | `user-group:read` |
| GET | `/api/user-groups/{id}` | 用户组详情 | `user-group:read` |
| POST | `/api/user-groups` | 创建用户组 | `user-group:create` |
| PUT | `/api/user-groups/{id}` | 更新用户组 | `user-group:update` |
| DELETE | `/api/user-groups/{id}` | 删除用户组 | `user-group:delete` |
| POST | `/api/user-groups/{id}/members` | 添加组成员 | `user-group:manage-members` |
| DELETE | `/api/user-groups/{id}/members/{userId}` | 移除组成员 | `user-group:manage-members` |
| PUT | `/api/user-groups/{id}/permissions` | 设置组权限 | `user-group:manage-permissions` |
| GET | `/api/user-groups/{id}/extras` | 获取用户组自定义字段 | `user-group:read` |
| PUT | `/api/user-groups/{id}/extras` | 更新用户组自定义字段 | `user-group:update` |

> **初始用户组**（`is_protected=true, is_member_locked=true`）不可编辑、不可删除、不可添加/移除成员。
>
> **超级管理员组**（`is_protected=true`）不可删除，但可修改成员和权限。
>
> **A 权限不可分配**给除"初始用户"外的任何用户组。

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
| 2xx | 登录 | 200=成功, 201=用户名或密码错误, 203=账号已注销, 204=账号被封禁, 205=登录频繁限制, 206=账号已被封禁, 207=账号未验证 |
| 3xx | 邮箱验证 | 300=成功, 301=无效链接, 302=链接过期, 303=无效类型, 304=用户不存在, 310=重发成功, 311=频率限制, 312=用户不存在, 313=已验证 |
| 4xx | 邮箱修改 | 400=请求成功, 401=邮箱相同, 402=邮箱已使用, 403=密码错误, 410=确认成功, 411=无效链接, 412=链接过期, 413=无效新邮箱 |
| 5xx | 密码修改 | 500=成功, 501=当前密码错误, 502=新旧密码相同 |
| 6xx | 用户操作 | 600=获取成功, 601=用户不存在, 610=更新成功, 611=用户不存在, 612=禁止修改, 613=用户名已存在, 614=邮箱已使用, 615=手机号已使用, 620=删除成功, 621=用户不存在, 630=列表成功 |
| 7xx | 头像 | 700=上传成功, 701=格式错误, 702=文件过大, 703=禁止上传, 710=删除成功, 711=头像不存在 |
| 8xx | 管理员 | 800=统计成功, 810=服务器状态成功 |
| 9xx | 邮件 | 900=发送成功, 901=发送失败 |
| 10xx | 权限 | 1000=权限不足, 1001=权限不存在, 1002=权限代码已存在, 1010=创建成功, 1011=更新成功, 1012=删除成功, 1013=不可修改特殊权限 |
| 11xx | 用户组 | 1100=用户组不存在, 1101=名称已存在, 1110=创建成功, 1111=更新成功, 1112=删除成功, 1113=受保护不可操作, 1120=成员已存在, 1121=成员不存在, 1122=添加成功, 1123=移除成功, 1124=成员锁定, 1130=设置权限成功, 1131=A权限限制, 1150=权限检查完成, 1160=扩展字段更新成功 |

## 用户状态

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | Normal | 正常用户，完整功能 |
| 2 | Banned | 被封禁，无法登录 |
| 3 | Unverified | 待验证，已注册但未验证邮箱 |
| 4 | Deleted | 软删除，账号已注销 |

## 数据库表

| 表名 | 说明 |
|------|------|
| `users` | 用户表（含自定义 JSON 扩展字段 `extras`） |
| `email_verification_tokens` | 邮箱验证令牌表 |
| `permissions` | 权限表（含特殊权限 A/B/C 和各 API 权限，`creator_id` 记录创建人） |
| `user_groups` | 用户组表（含自定义 JSON 扩展字段 `extras`，`is_member_locked` 标记成员锁定） |
| `user_group_members` | 用户组成员关联表 |
| `user_group_permissions` | 用户组权限关联表 |

## 项目结构

```
UCS/
├── Configuration/       # 配置类 (AppConfiguration, JwtConfig, SmtpConfig, AppSettings)
├── Data/               # 数据库上下文 (ApplicationDbContext)
├── DTOs/               # 数据传输对象 (ApiResponse, ResultCodes, PermissionCodes, UserDTOs, PermissionDTOs)
├── Endpoints/          # API 端点定义
│   ├── UserEndpoints.cs           # 用户端点
│   ├── AdminEndpoints.cs          # 管理员端点
│   ├── AvatarEndpoints.cs         # 头像端点
│   ├── PermissionEndpoints.cs     # 权限管理端点
│   └── UserGroupEndpoints.cs      # 用户组管理端点
├── Extensions/         # 扩展方法 (UserExtensions, PermissionEndpointExtensions)
├── Middleware/         # 中间件 (ExceptionHandlingMiddleware)
├── Models/             # 实体模型 (User, EmailVerificationToken, Permission, UserGroup, 枚举)
├── Services/           # 业务服务
│   ├── ConsoleService.cs          # 控制台输出
│   ├── EmailService.cs            # 邮件发送
│   ├── InitializationService.cs   # 启动初始化（含权限和用户组种子数据）
│   ├── JwtService.cs              # JWT Token 生成
│   ├── OnlineUserService.cs       # 在线用户跟踪（单例）
│   ├── PermissionService.cs       # 权限检查和管理
│   ├── TokenBlacklistService.cs   # JWT Token 黑名单（登出撤销）
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
3. 系统初始用户（uid=0）密码同样会输出到 `logs/init.log`，请妥善保管
4. **生产环境请务必修改 JWT Key**（至少 64 字符的随机字符串）
5. **生产环境请务必修改数据库密码**
6. 生产环境请配置 `AllowedOrigins` 限制跨域来源
7. 生产环境请配置 `ApiBaseUrl` 为 HTTPS 地址
8. 密码使用 BCrypt Work Factor 12 存储
9. 邮件发送频率限制：同一邮箱 1 分钟内只能请求一次
10. 注册频率限制：同一邮箱每分钟最多 5 次
11. 封禁用户无法登录，也无法获取 JWT Token
12. 头像端点已做路径遍历防护
13. 头像上传验证文件魔数签名（magic bytes），防止 Content-Type 伪装
14. 头像大于 512KB 时自动压缩，生成 256px 缩略图，减少存储和传输开销
15. 登录暴力破解防护：IP 20次/5分钟 + 账号 5次/5分钟双重限流
16. JWT Token 支持黑名单撤销，登出和密码修改时自动撤销当前 Token 和 Refresh Token
17. 支持 Refresh Token 机制（30 天有效），免密刷新 Access Token
18. 异常中间件在生产环境隐藏内部错误详情
19. A 权限仅限"初始用户"组，无法分配给其他用户组
20. 特殊权限（A/B/C）不可通过 API 创建、修改或删除
21. 权限检查结果带内存缓存（5 分钟过期），修改权限后自动清除对应用户缓存
22. 自定义权限记录创建人（`creator_id`），系统内置权限创建人为 null
23. 系统初始用户（uid=0）不可删除、不可修改、不可从初始用户组移除
24. 启动时自动检查 uid=0 是否在初始用户组，若不在则自动修复
25. SMTP 连接默认使用加密（端口 587=STARTTLS，465=SSL，其他端口默认 STARTTLS）

## 许可证

Apache License 2.0 - 参见 [LICENSE](LICENSE) 文件
