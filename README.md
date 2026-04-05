# User Management System

基于 ASP.NET Core 8 Minimal API + MySQL 8.0 构建的用户管理系统。

## 功能特性

- **用户认证**：JWT Bearer Token 认证
- **密码安全**：BCrypt 哈希存储
- **邮箱验证**：完整的注册验证和邮箱修改流程
- **头像管理**：支持上传、下载、删除用户头像
- **管理员功能**：用户统计、服务器状态监控、邮件测试
- **后台服务**：自动清理过期 Token
- **自动初始化**：首次启动自动创建管理员账户

## 技术栈

- ASP.NET Core 8 Minimal API
- Entity Framework Core 8
- MySQL 8.0 (Pomelo.EntityFrameworkCore.MySql)
- JWT Authentication
- MailKit (SMTP 邮件发送)
- BCrypt.Net-Next

## 快速开始

### 1. 配置数据库

编辑 `appsettings.json`：

```json
{
  "ConnectionStrings": {
    "Default": "server=localhost;database=userdb;user=root;password=your_password"
  }
}
```

### 2. 配置 SMTP

```json
{
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "your-email@example.com",
    "Password": "your-password",
    "From": "noreply@example.com",
    "FromName": "User Management System"
  }
}
```

### 3. 配置 JWT

```json
{
  "Jwt": {
    "Key": "YourSuperSecretKeyThatShouldBeAtLeast32CharactersLong!",
    "Issuer": "UserManagementAPI",
    "Audience": "UserManagementClient",
    "ExpiryMinutes": 120
  }
}
```

### 4. 运行

```bash
dotnet run
```

首次启动时，系统会自动：
- 创建数据库和表
- 生成初始管理员账户（用户名: `admin`，邮箱: `admin@system.local`）
- 在控制台和 `logs/init.log` 中显示随机生成的密码

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

### 用户端点（需要认证）

| 方法 | 路由 | 描述 | 权限 |
|------|------|------|------|
| GET | `/api/users/{id}` | 获取用户信息 | 本人或管理员 |
| PUT | `/api/users/{id}` | 完全更新用户 | 本人或管理员 |
| PATCH | `/api/users/{id}` | 部分更新用户 | 本人或管理员 |
| DELETE | `/api/users/{id}` | 软删除用户 | 仅管理员 |
| GET | `/api/users` | 分页查询用户列表 | 仅管理员 |
| POST | `/api/users/{id}/change-password` | 修改密码 | 本人 |
| POST | `/api/users/{id}/change-email` | 申请修改邮箱 | 本人 |
| POST | `/api/users/{id}/avatar` | 上传头像 | 本人或管理员 |
| DELETE | `/api/users/{id}/avatar` | 删除头像 | 本人或管理员 |

### 管理员端点（需要管理员权限）

| 方法 | 路由 | 描述 |
|------|------|------|
| GET | `/api/admin/stats/online-users` | 在线用户数量 |
| GET | `/api/admin/stats/new-users?days=30` | 新注册用户数（参数：天数） |
| GET | `/api/admin/stats/active-users?days=30` | 活跃用户数（参数：天数） |
| GET | `/api/admin/server/status` | 服务器状态 |
| POST | `/api/admin/email/test` | 发送测试邮件 |

**统计 API 参数说明**：

- `days`（Query 参数，整数）：统计天数范围，如 `days=30` 表示从 30 天前到今天的统计数据
- 默认返回数据中包含统计数量、查询天数、开始日期和结束日期

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

### 响应码说明

| 码段 | 类别 | 示例 |
|------|------|------|
| 0xx | 通用 | 0=成功, 1=错误, 2=验证错误, 3=未授权, 4=禁止 |
| 1xx | 注册 | 100=成功, 101=用户名存在, 102=邮箱已注册 |
| 2xx | 登录 | 200=成功, 201=用户不存在, 202=密码错误 |
| 3xx | 邮箱验证 | 300=成功, 301=无效链接, 302=链接过期 |
| 4xx | 邮箱修改 | 400=请求成功, 401=邮箱相同, 403=密码错误 |
| 5xx | 密码修改 | 500=成功, 501=当前密码错误 |
| 6xx | 用户操作 | 600=获取成功, 610=更新成功, 620=删除成功 |
| 7xx | 头像 | 700=上传成功, 701=格式错误, 702=文件过大 |
| 8xx | 管理员 | 800=统计成功, 810=服务器状态成功 |
| 9xx | 邮件 | 900=发送成功, 901=发送失败 |

## 用户状态

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | 正常 | 完整功能 |
| 2 | 封禁 | 可登录，禁止修改个人信息 |
| 3 | 待验证 | 已注册但未验证邮箱 |
| 4 | 软删除 | 账号已注销，无法登录 |

## 用户角色

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | 普通用户 | 基本权限 |
| 2 | 管理员 | 管理用户、查看统计等 |
| 3 | 访客 | 受限权限 |

## 项目结构

```
UCS/
├── Configuration/       # 配置类
├── Data/               # 数据库上下文
├── DTOs/               # 数据传输对象
├── Endpoints/          # API 端点定义
├── Extensions/         # 扩展方法
├── Middleware/         # 中间件
├── Models/             # 实体模型
├── Services/           # 业务服务
├── wwwroot/            # 静态文件
│   └── avatars/        # 头像存储
├── logs/               # 日志文件
├── appsettings.json     # 应用配置
├── LICENSE             # Apache License 2.0
└── Program.cs          # 入口点
```

## Swagger UI

开发环境下访问：`http://localhost:5000/swagger`

## 注意事项

1. 首次运行后请立即登录并修改管理员密码
2. 初始管理员密码会输出到控制台和 `logs/init.log`
3. 生产环境请修改 JWT Key 和数据库密码
4. 邮件发送频率限制：同一邮箱 1 分钟内只能请求一次

## 许可证

Apache License 2.0 - 参见 [LICENSE](LICENSE) 文件
