# User Management System

基于 ASP.NET Core 8 Minimal API 与 MySQL 8.0 的用户管理系统（UCS/UMS）。提供用户认证、权限管理、系统设置、邮件配置、子系统凭证签发，并内置基于 Blazor Server 的管理控制台。

## 能力范围

| 模块 | 内容 |
|------|------|
| 用户认证 | JWT Bearer（含 `sid`）；多设备会话免密刷新；邮箱验证与邮箱变更 |
| 账户安全 | 登录设备查看与踢出；登录历史审计；新设备/异地登录邮件提醒 |
| 口令 | BCrypt 哈希（Work Factor 12） |
| 权限 | 权限组 → 权限；A/B/C 特殊权限覆盖；权限组 level 层级 |
| 头像 | 上传/下载/删除；魔数校验；大图压缩与缩略图 |
| 系统管理 | 站点信息、LOGO、友链、登录/注册开关 |
| 服务器监控 | CPU/内存/网络/运行时间采样；指标 60s 落库，保留 8 天 |
| 邮件 | SMTP 配置存库并加密；测试发信 |
| 子系统凭证 | 签发 API Key；Secret 仅存 SHA256；子系统自注册权限（强制前缀） |
| 管理控制台 | Blazor Web App（Interactive Server）+ Ant Design Blazor |
| 后台任务 | 定时清理过期 Token、黑名单、限流记录、失效会话与登录日志 |
| 初始化 | 首次启动自动建表、写入权限与权限组种子、创建初始账号 |

## 技术栈

- ASP.NET Core 8 Minimal API
- Blazor Web App（Interactive Server），Ant Design Blazor
- Entity Framework Core 8（Pomelo.EntityFrameworkCore.MySql）
- MySQL 8.0
- JWT Bearer Authentication + 子系统 API Key 认证方案
- ASP.NET Core Data Protection（SMTP 口令加密）
- MailKit、BCrypt.Net-Next、Magick.NET
- Serilog.AspNetCore

## 运行环境要求

- .NET 8 SDK
- MySQL 8.0
- 反向代理（生产环境，用于 TLS 终止与 WebSocket 转发）

## 快速开始

### 1. 数据库连接

运行时从环境变量 `UCS_DB_CONNECTION` 读取连接串。

Windows（PowerShell，当前会话）：

```powershell
$env:UCS_DB_CONNECTION="server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;"
```

Windows（cmd）：

```bat
set UCS_DB_CONNECTION=server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;
```

Windows（永久生效，用户级）：

```powershell
[Environment]::SetEnvironmentVariable('UCS_DB_CONNECTION','server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;','User')
```

- 第三参数改为 `'Machine'` 为系统级变量，需管理员权限。设置永久变量后需重开终端（已打开的终端与 Visual Studio 进程不会获得新变量）。

图形界面方式：在「用户变量」新建 `UCS_DB_CONNECTION`，变量值为无引号纯文本。

Linux / macOS：

```bash
export UCS_DB_CONNECTION="server=127.0.0.1;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;"
```

systemd：

```ini
Environment="UCS_DB_CONNECTION=server=127.0.0.1;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;"
```

docker compose：

```yaml
environment:
  UCS_DB_CONNECTION: "server=db;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;"
```

解析优先级：

| 优先级 | 来源 |
|:---:|------|
| 1 | 环境变量 `UCS_DB_CONNECTION` |
| 2 | 配置键 `ConnectionStrings:Default`（等价于环境变量 `ConnectionStrings__Default`） |

验证取值：

```powershell
[Environment]::GetEnvironmentVariable('UCS_DB_CONNECTION','User')   # 永久值
$env:UCS_DB_CONNECTION                                              # 当前进程值
```

- 连接池参数：`MaximumPoolSize`、`MinimumPoolSize`、`ConnectionLifeTime`（秒）。
- `dotnet ef` 生成迁移时同样需要该环境变量（设计时会启动应用宿主）。
- 启动成功时，日志打印数据库目标与来源，不含凭据：`Database: 127.0.0.1/ucsdb via env:UCS_DB_CONNECTION`。启动失败时，原因写入控制台标准错误与 `logs/` 目录下的日志文件。

### 2. 邮件服务（SMTP）

SMTP 配置存于数据库，通过管理控制台「邮件服务」页面维护：服务器地址、端口、SSL、账号、密码、发件人、显示名。保存后即时生效，可发送测试邮件验证连通性。

- 配置文件中不含 `Smtp` 段落，启动时不从配置文件导入。
- 端口规则：587 使用 STARTTLS，465 使用 SSL 直连，其他端口由 `UseSsl` 决定。
- 密码经 Data Protection 加密后入库。密钥环默认持久化到 `keys/`（可用 `AppSettings:DataProtectionKeysPath` 指定）。密钥环丢失将导致已存密码无法解密，邮件服务降级为「未配置」，需重新录入。

### 3. JWT

```json
{
  "Jwt": {
    "Key": "<至少 64 字符的随机字符串>",
    "Issuer": "UserManagementAPI",
    "Audience": "UserManagementClient",
    "ExpiryMinutes": 120,
    "RefreshTokenDays": 30
  }
}
```

| 键 | 说明 |
|----|------|
| `ExpiryMinutes` | Access Token 有效期（分钟） |
| `RefreshTokenDays` | 会话（Refresh Token）有效期（天数），默认 30 |

- Access Token 携带 `sid` 声明指向登录会话；会话被撤销（登出、踢出、封禁）后，该 Token 会在数秒内失效，无需等待过期。

### 4. CORS

```json
{
  "AppSettings": {
    "AllowedOrigins": "https://example.com,https://app.example.com"
  }
}
```

- `AllowedOrigins` 为空时：开发环境允许任意来源；生产环境阻止全部跨域请求。
- 多来源以逗号分隔。生产环境需显式配置。

### 5. API 基础 URL

```json
{
  "AppSettings": {
    "ApiBaseUrl": "https://api.yourdomain.com"
  }
}
```

用于生成验证邮件中的链接（用户点击后回到本服务）。生产环境应使用 HTTPS 地址。

- 该项**只影响邮件链接**，与管理控制台的回环调用无关（回环地址自动解析，见第 6 节）。
- 若邮箱验证功能未启用，可保持模板默认值或留空。

### 6. 应用参数

```json
{
  "AppSettings": {
    "TokenExpiryMinutes": 20,
    "AdminPasswordLength": 12,
    "SelfBaseUrl": "",
    "DataProtectionKeysPath": "keys"
  }
}
```

| 键 | 说明 |
|----|------|
| `TokenExpiryMinutes` | 邮箱验证 Token 有效期（分钟），默认 20 |
| `AdminPasswordLength` | 首次启动生成的初始账号口令长度，默认 12 |
| `SelfBaseUrl` | 控制台回环调用本进程 REST API 的基地址。**可选覆盖项，一般留空即可**：留空时自动使用进程真实监听地址（多地址优先 `http`；通配地址 `0.0.0.0` / `[::]` / `*` / `+` 规范化为 `localhost`）。仅当自动解析出的地址无法从本机访问时才需显式填写 |
| `DataProtectionKeysPath` | Data Protection 密钥环目录，默认 `keys`，相对应用工作目录 |

- 回环调用地址取自**进程真实监听的地址**（与 `launchSettings` / `--urls` / `ASPNETCORE_URLS` / `HTTP_PORTS` / IIS-ANCM 动态端口无关，均能正确解析），因此**无需配置任何环境变量**即可在本地 `dotnet run` 与生产环境工作。
- 解析不到任何 HTTP/HTTPS 监听地址时应用**启动即失败**并在控制台与 `logs/` 给出指引，不会回落到一个没人监听的地址。此时可按提示设置 `AppSettings:SelfBaseUrl`（或环境变量 `AppSettings__SelfBaseUrl`）为**本机可达**的地址。
- 回环地址的 Host 会在启动后自动并入 `HostFiltering` 白名单，因此 `AllowedHosts` 仅登记对外域名（如 `api.yourdomain.com`）时也能正常工作。

#### 权限缓存与会话安全

权限缓存采用「全局版本号」策略：缓存键带版本前缀，权限写操作只把版本号自增一次，其他实例在 `VersionCacheSeconds` 内自然读到新版本并回源查库。因此修改权限（含批量改组内成员）后，所有实例在**秒级**返回新结果，且不再逐个用户清缓存。

```json
{
  "Permissions": {
    "CacheTtlMinutes": 2,
    "VersionCacheSeconds": 1,
    "ClientPollSeconds": 30
  },
  "Security": {
    "SessionValidationCacheSeconds": 5,
    "LoginHistoryRetentionDays": 180,
    "LocationHistoryDays": 90,
    "AlertOnNewDevice": true,
    "AlertOnNewLocation": true,
    "AlertDedupHours": 24,
    "Ip2RegionDbPath": "App_Data/ip2region.xdb"
  }
}
```

| 键 | 说明 |
|----|------|
| `Permissions:CacheTtlMinutes` | 权限缓存兜底 TTL（分钟），默认 2；正常情况下由版本号失效，不依赖该值 |
| `Permissions:VersionCacheSeconds` | 版本号内存缓存秒数，默认 1，等于跨实例生效延迟 |
| `Permissions:ClientPollSeconds` | 管理控制台轮询权限版本的间隔（秒），默认 30；`<= 0` 关闭自动刷新 |
| `Security:SessionValidationCacheSeconds` | 会话有效性校验结果缓存秒数，默认 5；只缓存「有效」结论，撤销后最长 5 秒生效 |
| `Security:LoginHistoryRetentionDays` | 登录日志保留天数，默认 180，由清理任务定期删除 |
| `Security:LocationHistoryDays` | 异地登录判定回溯天数，默认 90 |
| `Security:AlertOnNewDevice` | 新设备登录时发送邮件提醒，默认开启 |
| `Security:AlertOnNewLocation` | 异地登录时发送邮件提醒，默认开启 |
| `Security:AlertDedupHours` | 同一设备+地点的提醒去重窗口（小时），默认 24 |
| `Security:Ip2RegionDbPath` | 离线 IP 库路径，相对内容根目录；缺失时归属地显示为「未知」且不影响登录 |

- IP 归属地使用离线库 `ip2region.xdb`（[ip2region](https://github.com/lionsoul2014/ip2region) 项目，取 `data/ip2region.xdb`）。把文件放到 `App_Data/ip2region.xdb` 即可，发布时会随输出复制；未放置时系统仍可正常运行，仅归属地解析降级为「未知」。
- 登录风险提醒走有界内存队列异步发送，登录接口不等待邮件结果；SMTP 未配置或发送失败只记日志，不影响登录。
- 登录审计只记录事件与网络环境（IP、归属地、设备名、UA），**不记录密码与令牌明文**。

### 7. 运行

```bash
dotnet run                       # 开发环境
dotnet run --environment Production
```

首次运行前复制 `Properties/launchSettings.template.json` 为 `Properties/launchSettings.json`，替换 `YOUR_DB_USER`、`YOUR_DB_PASSWORD`。

宿主启动完成后打印监听地址（通配地址规范化为 `localhost`），横幅中的「回环基地址」即控制台访问自身 API 所用的地址，与实际监听地址同源：

```
  ──────────────────────────────────────────────
    管理控制台: http://localhost:5208/
    Swagger UI: http://localhost:5208/swagger
    Health:     http://localhost:5208/health
    回环基地址: http://localhost:5208  （取自进程真实监听地址，可用 AppSettings:SelfBaseUrl 覆盖）
  ──────────────────────────────────────────────
```

端口以**进程实际监听地址**为准，来源可以是 `Properties/launchSettings.json` 的 `applicationUrl`（默认 5208）、`--urls`、`ASPNETCORE_URLS`、`HTTP_PORTS` 或 IIS/ANCM 动态端口；无法解析出任何监听地址时启动直接失败并给出指引。

Visual Studio 启动行为（由 `Properties/launchSettings.json` 控制）：

| 配置 | 作用 |
|------|------|
| `launchBrowser: true` | 启动时打开浏览器 |
| 未设置 `launchUrl` | 打开应用根路径（控制台 `/`）；设为 `swagger` 则打开接口文档 |
| `environmentVariables.UCS_DB_CONNECTION` | 开发环境连接串 |

#### 首次启动行为

首次启动自动执行：

- 应用 EF Core 迁移（全新库直接建表；旧版 `EnsureCreated` 库建立迁移基线后升级）
- 初始化权限表（A/B/C 特殊权限与各 API 权限）
- 初始化权限组（初始用户、超级管理员、管理员、普通用户）
- 创建系统初始用户（uid=0，用户名 `system`，持有 A 权限）
- 生成超级管理员账户（用户名 `admin`，邮箱 `admin@system.local`，属「超级管理员」组）
- 输出随机生成的初始口令至启动日志

初始口令应在首次登录后修改。

## 管理控制台

访问 `http://<host>:<port>/`，使用管理员账号登录。

| 页面 | 路由 | 所需权限 |
|------|------|---------|
| 登录 | `/login` | 公开 |
| 仪表盘 | `/` | 登录即可（统计卡片需 `admin:stats`） |
| 服务器状态 | `/server` | `admin:server` |
| 用户管理 | `/users` | `user:list`（编辑/删除/分组/重置密码分别需对应权限） |
| 权限组 | `/user-groups` | `user-group:read`（及各操作对应权限） |
| 权限 | `/permissions` | `permission:read`（及各操作对应权限） |
| 系统设置 | `/settings` | `system-setting:read` / `system-setting:update` |
| 邮件服务 | `/settings/smtp` | `smtp:read` / `smtp:update` |
| 子系统 | `/subsystems` | `subsystem:read`（及各操作对应权限） |
| 无权限提示 | `/forbidden` | 登录即可 |
| 我的权限 | `/my-permissions` | 登录即可 |

页面即上表所列路径，登录后按自身权限自动显隐菜单与操作按钮；权限变更后约 30 秒内自动刷新，无需手动重新加载页面。

## 权限体系

### 概述

系统采用基于权限组的权限控制：每个 API 端点对应一个权限代码，用户经所属权限组获得权限。用户可同时属于多个权限组，最终权限为所有组权限的并集。初始用户（uid=0）始终保留在「初始用户」组，不可移除。

### 特殊权限

| 权限 | 代码 | 说明 |
|------|------|------|
| A | `PERM_A` | 全站所有 API（含 B、C 覆盖范围）。仅「初始用户」组持有，不可分配给其他组 |
| B | `PERM_B` | 权限组管理权限，可管理权限表与权限组（除 A/B/C） |
| C | `PERM_C` | 服务器配置管理权限，可使用服务器管理相关 API |

### 预设权限组

| 权限组 | 说明 | 成员锁定 | 可删除 | 基本用户权限 | 持有权限 |
|--------|------|:---:|:---:|:---:|---------|
| 初始用户 | 仅 uid=0 成员，持有 A | 是（不可增删成员） | 否 | 否 | `PERM_A` |
| 超级管理员 | 除 A 外全部权限 | 否 | 否 | 否 | `PERM_B`、`PERM_C` + 全部 API 权限 |
| 管理员 | 用户管理与基本管理权限 | 否 | 是 | 否 | 用户管理（含重置密码）+ 统计 + 头像 + 权限组查看 + 登出 + 系统设置 |
| 普通用户 | 基本用户权限 | 否 | 是 | 是 | 查看/更新自己 + 修改密码/邮箱 + 头像 + 登出 |

- 初始权限组：不可编辑（名称、描述、权限）、不可删除、不可增删成员。
- 超级管理员组：不可删除，成员与权限可修改。

### 权限代码

| 分类 | 代码 | 说明 |
|------|------|------|
| special | `PERM_A` | 全站所有 API |
| special | `PERM_B` | 权限表与权限组管理（除 A/B/C） |
| special | `PERM_C` | 服务器管理 API |
| user | `user:read` | 查看用户信息 |
| user | `user:create` | 管理员直接创建用户（可跳过邮箱验证） |
| user | `user:update` | 更新用户信息 |
| user | `user:delete` | 删除用户 |
| user | `user:list` | 查看用户列表 |
| user | `user:change-password` | 修改密码 |
| user | `user:change-email` | 修改邮箱 |
| user | `user:logout` | 登出 |
| user | `user:reset-password` | 管理员重置指定用户登录密码 |
| security | `session:read` | 查看本人的登录设备列表 |
| security | `session:revoke` | 踢出本人指定设备或全部其他设备 |
| security | `login-history:read` | 查看本人的登录、刷新、登出与被踢记录 |
| avatar | `avatar:upload` | 上传头像 |
| avatar | `avatar:delete` | 删除头像 |
| admin | `admin:stats` | 管理员统计（C 权限覆盖） |
| admin | `admin:server` | 服务器状态与配置（C 权限覆盖） |
| permission | `permission:read` | 查看权限列表（B 权限覆盖） |
| permission | `permission:create` | 创建权限（B 权限覆盖） |
| permission | `permission:update` | 更新权限（B 权限覆盖） |
| permission | `permission:delete` | 删除权限（B 权限覆盖） |
| user-group | `user-group:read` | 查看权限组（B 权限覆盖） |
| user-group | `user-group:create` | 创建权限组（B 权限覆盖） |
| user-group | `user-group:update` | 更新权限组（B 权限覆盖） |
| user-group | `user-group:delete` | 删除权限组（B 权限覆盖） |
| user-group | `user-group:manage-members` | 增删权限组成员（B 权限覆盖） |
| user-group | `user-group:manage-permissions` | 设置权限组权限（B 权限覆盖） |
| system-setting | `system-setting:read` | 查看系统设置 |
| system-setting | `system-setting:update` | 更新站点名称、LOGO、友链、登录/注册开关 |
| smtp | `smtp:read` | 查看邮件配置（密码脱敏） |
| smtp | `smtp:update` | 修改邮件配置并发送测试邮件 |
| subsystem | `subsystem:read` | 查看子系统与凭证信息 |
| subsystem | `subsystem:create` | 登记子系统并签发凭证 |
| subsystem | `subsystem:update` | 更新子系统信息、启停、重置密钥 |
| subsystem | `subsystem:delete` | 删除子系统并使其凭证立即失效 |

### 权限检查逻辑

1. 从 JWT 获取用户 ID。
2. 查询用户所属全部权限组及权限（内存缓存，键带全局权限版本前缀，版本变化即失效，兜底 TTL 2 分钟）。
3. 检查顺序：
   - 拥有 A 权限 → 通过
   - API 属于 B 覆盖范围（权限管理 + 权限组管理）且拥有 B → 通过
   - API 属于 C 覆盖范围且拥有 C → 通过
   - 拥有该 API 对应具体权限 → 通过
   - 否则 → 403 Forbidden
4. 部分 API 支持 `allowSelf`：访问自己的资源时跳过权限检查。

## API 端点

### 公开端点（无需认证与权限）

| 方法 | 路由 | 描述 | 请求体/参数 |
|------|------|------|-------------|
| POST | `/api/users/register` | 用户注册 | `{username, email, password, extras?}` |
| POST | `/api/users/login` | 用户登录 | `{login, password}` |
| POST | `/api/users/refresh` | 刷新 Token（免登录） | `{refreshToken}` |
| GET | `/api/users/verify-email` | 验证邮箱 | Query: `?token=xxx` |
| POST | `/api/users/resend-verification` | 重新发送验证邮件 | `{email}` |
| GET | `/api/users/verify-email-change` | 确认修改邮箱 | Query: `?token=xxx` |
| GET | `/api/avatars/{filename}` | 获取头像（`?type=thumb` 获取缩略图） | - |
| GET | `/api/permissions/custom` | 获取自定义权限列表 | - |
| GET | `/api/system/info` | 获取系统基本信息 | - |
| GET | `/api/system/friend-links` | 获取友情链接 | - |
| GET | `/health` | 健康检查 | - |

- Swagger UI 仅开发环境注册，访问 `/swagger` 在线调试接口。

请求验证规则：

- `username`：3-64 个字符
- `email`：有效邮箱格式，最大 128 字符
- `password`：8-128 个字符

- 注册限流：同一邮箱每分钟最多 5 次。
- 新注册用户自动加入所有开启「基本用户权限」（`is_default_for_new_users = true`）的权限组。内置「普通用户」组默认开启；若所有组关闭该标记，新用户不加入任何权限组。

### 用户端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | allowSelf |
|------|------|------|---------|-----------|
| GET | `/api/users/me/permissions` | 获取当前用户展开后的有效权限码 | 认证即可 | - |
| GET | `/api/users/{id}` | 获取用户信息 | `user:read` | 是 |
| GET | `/api/users/{id}/groups` | 获取用户所属权限组 | `user:read` | 是 |
| PUT | `/api/users/{id}/groups` | 批量设置用户所属权限组 | `user-group:manage-members` | 否 |
| PUT | `/api/users/{id}` | 完全更新用户 | `user:update` | 是 |
| PATCH | `/api/users/{id}` | 部分更新用户 | `user:update` | 是 |
| DELETE | `/api/users/{id}` | 软删除用户 | `user:delete` | 否 |
| GET | `/api/users` | 分页查询用户列表 | `user:list` | - |
| POST | `/api/users/{id}/change-password` | 修改密码 | `user:change-password` | 否（仅限本人） |
| POST | `/api/users/{id}/change-email` | 申请修改邮箱 | `user:change-email` | 否（仅限本人） |
| POST | `/api/users/{id}/avatar` | 上传头像 | `avatar:upload` | 是 |
| DELETE | `/api/users/{id}/avatar` | 删除头像 | `avatar:delete` | 是 |
| POST | `/api/users/logout` | 用户登出（仅撤销当前设备会话） | `user:logout` | 否 |
| GET | `/api/users/me/sessions` | 查看本人登录设备（含「当前设备」标记） | `session:read` | - |
| DELETE | `/api/users/me/sessions/{sessionId}` | 踢出本人指定设备 | `session:revoke` | - |
| DELETE | `/api/users/me/sessions` | 踢出本人除当前设备外的全部会话 | `session:revoke` | - |
| GET | `/api/users/me/login-history` | 分页查询本人登录历史 | `login-history:read` | - |

- `allowSelf`：路由参数 `{id}` 与当前用户 ID 匹配时跳过权限检查。change-password 与 change-email 不使用 allowSelf，而是要求权限并手动校验仅限本人。
- 邮箱修改：普通用户不能通过 PUT/PATCH 直接修改邮箱，须走 change-email 流程（验证密码 → 发送验证邮件 → 确认修改）；管理员可直接通过 PUT 修改。
- 手机号：仅在 PUT/PATCH 更新时支持，最大 20 字符，注册时不支持。
- 头像上传：仅支持 JPG/PNG/GIF/WebP，最大 6MB。大于 512KB 时压缩并生成 256px 缩略图，否则缩略图等于原图。
- `GET /api/users/me/permissions` 返回 `{ userId, codes: [...] }`，`codes` 为展开 A/B/C 覆盖后的有效权限码，供前端控制菜单与按钮显隐。

会话与登录历史：

- `GET /api/users/me/sessions` 返回 `{ items: [{ id, deviceName, deviceType, ip, location, userAgent, createdAt, lastActiveAt, isCurrent, isActive }], total }`；`isCurrent` 标记当前请求所在的设备会话。
- `DELETE /api/users/me/sessions/{sessionId}` 只能撤销本人会话，撤销他人会话返回 404；撤销后该设备的 Access Token 在数秒内失效。
- `DELETE /api/users/me/sessions` 保留当前设备、撤销其余全部会话，返回 `{ revoked: n }`。
- `GET /api/users/me/login-history` 支持 `page`、`pageSize`（1-100，默认 20）、`success`（`true`/`false`）、`eventType`（`login` / `refresh` / `logout` / `revoked`），返回 `{ items, total, page, pageSize }`；提醒邮件的发送记录（`alert`）不对外返回。
- 同一「浏览器 + 操作系统」组合视为同一设备：新登录会顶掉该设备上的旧会话（`replaced_by_new_device`），不同设备可同时在线。

### 管理员端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | 特殊覆盖 |
|------|------|------|---------|---------|
| GET | `/api/admin/stats/total-users` | 总用户数量（排除已删除） | `admin:stats` | C |
| GET | `/api/admin/stats/online-users` | 在线用户数量 | `admin:stats` | C |
| GET | `/api/admin/stats/new-users` | 新注册用户数 | `admin:stats` | C |
| GET | `/api/admin/stats/active-users` | 活跃用户数 | `admin:stats` | C |
| GET | `/api/admin/server/status` | 服务器状态 | `admin:server` | C |
| POST | `/api/admin/email/test` | 发送测试邮件（使用库中 SMTP 配置） | `admin:server` | C |
| POST | `/api/admin/users` | 创建用户（不发送验证邮件；密码留空则生成） | `user:create` | - |
| POST | `/api/admin/users/{id}/reset-password` | 重置指定用户密码（不传新密码则生成并一次性返回） | `user:reset-password` | - |
| GET | `/api/admin/users/{id}/sessions` | 查看指定用户的登录设备 | `user:read` | - |
| DELETE | `/api/admin/users/{id}/sessions/{sessionId}` | 踢出指定用户的某个设备 | `user:update` | - |

- 统计接口支持 `days`（正整数）Query 参数。

### 权限管理端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | 特殊覆盖 |
|------|------|------|---------|---------|
| GET | `/api/permissions` | 获取所有权限 | `permission:read` | B |
| POST | `/api/permissions/check` | 权限检查 | 认证即可（查他人需 `permission:read`） | - |
| POST | `/api/permissions` | 创建权限 | `permission:create` | B |
| PUT | `/api/permissions/{id}` | 更新权限 | `permission:update` | B |
| DELETE | `/api/permissions/{id}` | 删除权限 | `permission:delete` | B |

- B 权限持有者可管理权限表与权限组，但不可创建、修改或删除 A/B/C 特殊权限。

### 权限组管理端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/user-groups` | 权限组列表（分页） | `user-group:read` |
| GET | `/api/user-groups/{id}` | 权限组详情 | `user-group:read` |
| POST | `/api/user-groups` | 创建权限组 | `user-group:create` |
| PUT | `/api/user-groups/{id}` | 更新权限组 | `user-group:update` |
| DELETE | `/api/user-groups/{id}` | 删除权限组 | `user-group:delete` |
| POST | `/api/user-groups/{id}/members` | 添加组成员 | `user-group:manage-members` |
| DELETE | `/api/user-groups/{id}/members/{userId}` | 移除组成员 | `user-group:manage-members` |
| PUT | `/api/user-groups/{id}/permissions` | 设置组权限 | `user-group:manage-permissions` |

- 初始权限组（`is_protected=true, is_member_locked=true`）不可编辑、删除、增删成员；超级管理员组（`is_protected=true`）不可删除但可修改成员与权限；A 权限不可分配给除「初始用户」外的任何权限组。
- 基本用户权限（`is_default_for_new_users`，布尔，默认 `false`）：为 `true` 时新注册用户默认加入该组，可同时为多个组开启；成员锁定的组即使开启也不会被自动分配。内置「普通用户」组默认开启。

### 系统管理端点

公开接口（无需认证）：

| 方法 | 路由 | 描述 |
|------|------|------|
| GET | `/api/system/info` | 系统基本信息（站点名、LOGO URL、登录/注册开关） |
| GET | `/api/system/friend-links` | 友情链接列表 |

管理接口（认证 + `system-setting` 权限）：

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| PUT | `/api/admin/system/info` | 更新系统基本信息 | `system-setting:update` |
| PUT | `/api/admin/system/friend-links` | 更新友情链接 | `system-setting:update` |
| GET | `/api/admin/system/settings` | 获取所有系统设置 | `system-setting:read` |
| GET | `/api/admin/system/settings/{key}` | 获取单个设置 | `system-setting:read` |
| PUT | `/api/admin/system/settings/{key}` | 更新单个设置 | `system-setting:update` |

- 系统信息键：`site_name`、`logo_url`、`allow_login`、`allow_register`，默认允许。友情链接以 JSON 数组存储，每项含 `name`、`url`、`description`（可选）、`logoUrl`（可选）。

### 邮件服务配置端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/admin/smtp/config` | 获取邮件配置（密码脱敏，仅返回 `hasPassword`） | `smtp:read` |
| PUT | `/api/admin/smtp/config` | 保存邮件配置（密码留空表示不修改） | `smtp:update` |
| POST | `/api/admin/smtp/test` | 使用当前配置发送测试邮件 | `smtp:update` |

- 配置存于 `smtp_config` 单行表，密码经 Data Protection 加密，保存后立即生效（缓存自动失效）。启用邮件服务时 `host`、`port`、`fromAddress` 必填，否则返回 `code=2`。

### 子系统管理端点（认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/subsystems` | 子系统列表（分页 + 关键字搜索） | `subsystem:read` |
| GET | `/api/subsystems/{id}` | 子系统详情 | `subsystem:read` |
| POST | `/api/subsystems` | 登记子系统并签发凭证 | `subsystem:create` |
| PUT | `/api/subsystems/{id}` | 更新子系统信息（不影响凭证） | `subsystem:update` |
| PUT | `/api/subsystems/{id}/status` | 启用/停用子系统 | `subsystem:update` |
| POST | `/api/subsystems/{id}/regenerate-secret` | 重新生成凭证（旧凭证立即失效） | `subsystem:update` |
| DELETE | `/api/subsystems/{id}` | 删除子系统 | `subsystem:delete` |

- 凭证策略：`apiKey` 明文存储用于定位与展示；`apiSecret` 仅存 SHA256 哈希，明文只在创建或重置时返回一次。
- 子系统调用方式：请求头携带 `X-Api-Key` + `X-Api-Secret`，可访问「仅需认证、无需用户权限」的端点（即 `POST /api/permissions/check`，须显式传入 `userId`）。详见接入指南。

## API 响应格式

所有响应使用统一 JSON 格式：

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

### 响应码

| 码段 | 类别 | 主要码值 |
|------|------|---------|
| 0xx | 通用 | 0=成功, 1=错误, 2=验证错误, 3=未授权, 4=禁止, 5=不存在, 6=内部错误, 7=频率限制 |
| 1xx | 注册 | 100=成功, 101=用户名存在, 102=邮箱已注册, 103=手机号已使用 |
| 2xx | 登录 | 200=成功, 201=用户名或密码错误, 203=账号已注销, 204=账号被封禁, 205=登录频繁限制, 206=账号已被封禁, 207=账号未验证 |
| 3xx | 邮箱验证 | 300=成功, 301=无效链接, 302=链接过期, 303=无效类型, 304=用户不存在, 310=重发成功, 311=频率限制, 312=用户不存在, 313=已验证 |
| 4xx | 邮箱修改 | 400=请求成功, 401=邮箱相同, 402=邮箱已使用, 403=密码错误, 410=确认成功, 411=无效链接, 412=链接过期, 413=无效新邮箱 |
| 5xx | 密码修改 | 500=成功, 501=当前密码错误, 502=新旧密码相同 |
| 6xx | 用户操作 | 600=获取成功, 601=用户不存在, 610=更新成功, 611=用户不存在, 612=禁止修改, 613=用户名已存在, 614=邮箱已使用, 615=手机号已使用, 616=状态限制, 617=权限级别不足, 620=删除成功, 621=用户不存在, 630=列表成功 |
| 7xx | 头像 | 700=上传成功, 701=格式错误, 702=文件过大, 703=禁止上传, 710=删除成功, 711=头像不存在 |
| 8xx | 管理员 | 800=统计成功, 810=服务器状态成功 |
| 9xx | 邮件 | 900=发送成功, 901=发送失败 |
| 10xx | 权限 | 1000=权限不足, 1001=权限不存在, 1002=权限代码已存在, 1010=创建成功, 1011=更新成功, 1012=删除成功, 1013=不可修改特殊权限 |
| 11xx | 权限组 | 1100=权限组不存在, 1101=名称已存在, 1110=创建成功, 1111=更新成功, 1112=删除成功, 1113=受保护不可操作, 1120=成员已存在, 1121=成员不存在, 1122=添加成功, 1123=移除成功, 1124=成员锁定, 1130=设置权限成功, 1131=A权限限制, 1150=权限检查完成 |

## 用户状态

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | Normal | 正常用户 |
| 2 | Banned | 被封禁，无法登录 |
| 3 | Unverified | 待验证，已注册但未验证邮箱 |
| 4 | Deleted | 软删除 |

## 权限组级别（Level）

每个权限组含 `level` 字段，数值越小权限越高：

| 权限组 | Level | 说明 |
|--------|-------|------|
| 初始用户 | 0 | 最高级别，不可修改 |
| 超级管理员 | 2 | |
| 管理员 | 10 | |
| 普通用户 | 20 | |
| 新建权限组 | 100 | 默认最低 |
| 无权限组用户 | `int.MaxValue` | 视为最低权限 |

判定规则：

- 操作者级别取其所属权限组中最小的 level，目标用户同理。
- 目标级别 ≤ 操作者级别 → 拒绝（`617`）。
- 分配/新建/编辑/删除权限组时，目标组级别须严格大于操作者级别（数值更大）。
- 添加/移除权限组成员时，目标用户级别须严格大于操作者级别；因此移除自己等同「管理同级用户」而被拒绝。
- 初始用户组（level 0）级别不可修改。

覆盖范围：`PUT/PATCH /api/users/{id}`、`PUT /api/users/{id}/groups`、`POST /api/admin/users/{id}/reset-password`、`DELETE /api/users/{id}`、权限组的创建/更新/删除，以及权限组成员的添加/移除。操作自己的资料不受级别限制，但组成员操作不豁免。

## 数据库表

| 表名 | 说明 |
|------|------|
| `users` | 用户（含 JSON 扩展字段 `extras`） |
| `email_verification_tokens` | 邮箱验证令牌 |
| `permissions` | 权限（含 A/B/C 与各 API 权限；`creator_id` 创建人，`subsystem_id` 归属子系统） |
| `user_groups` | 权限组（`level`、`is_protected`、`is_member_locked`、`is_default_for_new_users`） |
| `user_group_members` | 权限组成员关联 |
| `user_group_permissions` | 权限组权限关联 |
| `system_settings` | 系统设置（键值对，支持 string/bool/json） |
| `revoked_tokens` | JWT 黑名单（撤销的 jti，按过期时间清理） |
| `rate_limit_entries` | 限流计数（原子 upsert 递增） |
| `user_sessions` | 登录会话（每设备一条；`refresh_token_hash` 唯一、`device_key`、`last_jti`、`revoked_at`/`revoked_reason`） |
| `login_logs` | 登录审计流水（登录/刷新/登出/被踢 + 结果、失败原因、IP、归属地、设备） |
| `cache_versions` | 缓存版本号（`scope` 主键，权限与用户状态缓存键的版本前缀） |
| `smtp_config` | 邮件服务配置（单行，主键固定为 1；密码加密存储） |
| `subsystems` | 子系统凭证（`code`/`api_key` 唯一索引；`api_secret_hash` 仅存 SHA256） |

- 表结构由 EF Core Migrations 管理（见 `Migrations/`），服务启动时自动应用迁移。
- 在线用户数不再单独建表，改由 `user_sessions.last_active_at`（30 分钟内活跃）派生，支持一个账号多设备在线。

> **升级提示（破坏性变更）**：本版本把 Refresh Token 从 `users` 表迁移到 `user_sessions` 并删除 `online_users` 表。升级后**所有已登录用户需重新登录一次**（旧 Refresh Token 无法继续刷新，旧 Access Token 到期后需重新登录），无需手工迁移数据。

## 项目结构

```
UCS/
├── Auth/               # 管理前端认证与授权状态
│   ├── AuthSchemes.cs                       # 认证方案名（Bearer / SubsystemKey / MultiAuth）
│   ├── TokenStore.cs                        # circuit 级会话状态
│   ├── SessionPersistence.cs                # 会话的浏览器侧持久化
│   ├── JwtAuthenticationStateProvider.cs    # 认证状态提供者（含刷新恢复）
│   └── PermissionState.cs                   # 有效权限集合缓存
├── Components/         # Blazor 管理控制台（Interactive Server）
│   ├── App.razor / Routes.razor / _Imports.razor
│   ├── Layout/                              # MainLayout / NavMenu / TopBar / EmptyLayout
│   ├── Shared/                              # PageContainer / PermissionGate / RedirectToLogin
│   │                                        # UiIcon / UiIconData（Lucide 图标）
│   └── Pages/                               # 登录/仪表盘/服务器/用户/权限组/权限/设置/邮件/子系统/403
├── Configuration/      # AppConfiguration, JwtConfig, SmtpConfig, AppSettings, DatabaseConnection
├── Data/               # ApplicationDbContext, DatabaseMigrator
├── DTOs/               # ApiResponse, ResultCodes, PermissionCodes, PermissionDTOs,
│                       # UserDTOs, SystemSettingDTOs, SessionDTOs, CacheScopes
├── Endpoints/          # 按业务域垂直切片
│   ├── AuthEndpoints.cs / UserProfileEndpoints.cs / AdminEndpoints.cs
│   ├── SessionEndpoints.cs（登录设备 / 登录历史）
│   ├── AvatarEndpoints.cs / PermissionEndpoints.cs / UserGroupEndpoints.cs
│   ├── SystemSettingEndpoints.cs
│   ├── SmtpConfigEndpoints.cs / SubsystemEndpoints.cs
├── Exceptions/         # BusinessException（服务层显式业务错误）
├── Extensions/         # UserExtensions, PermissionEndpointExtensions,
│                       # ServiceCollectionExtensions, WebApplicationExtensions
├── Logging/            # SerilogConfig, LoggingCircuitHandler
├── Middleware/         # ExceptionHandlingMiddleware
├── Migrations/         # EF Core 迁移（启动时自动应用）
├── Models/             # 实体模型（User, EmailVerificationToken, Permission, UserGroup,
│                       # UserGroupMember, UserGroupPermission, SystemSetting,
│                       # RevokedToken, RateLimitEntry, SmtpConfig, Subsystem,
│                       # UserSession, LoginLog, CacheVersion）
├── App_Data/           # 离线 IP 库放置目录（ip2region.xdb，可选）
├── docs/               # INTEGRATION_GUIDE.md（业务系统接入指南）
├── Services/           # 业务服务
│   ├── ConsoleService.cs / EmailService.cs
│   ├── InitializationService.cs / JwtService.cs
│   ├── PermissionService.cs / RateLimitService.cs / CacheVersionService.cs
│   ├── SessionService.cs（会话与登录审计）
│   ├── SystemMonitorService.cs / SystemSettingService.cs
│   ├── TokenBlacklistService.cs / TokenCleanupService.cs / TokenService.cs
│   ├── UserGroupService.cs / UserService.cs
│   ├── Security/       # Ip2RegionLocationResolver, DeviceInfoFactory,
│   │                   # LoginRiskEvaluator, LoginAlertQueue, LoginAlertBackgroundService
│   └── Api/            # 管理前端 API 访问层
│       ├── UcsApiClient.cs / SelfBaseUrlProvider.cs
│       ├── ApiResult.cs / EndpointDataModels.cs
├── wwwroot/            # avatars/（头像存储）、app.css（样式与设计令牌）
├── logs/               # Serilog 日志（access/error/app 每日滚动）
├── appsettings.json    # 应用配置
├── LICENSE             # Apache License 2.0
└── Program.cs          # 入口点
```

## 安全说明

1. 首次运行后应登录并修改管理员及初始用户口令。
2. 密码使用 BCrypt Work Factor 12 存储。
3. 初始账号口令在首次启动时写入启动日志。
4. 登录暴力破解防护：IP 20 次/5 分钟 + 账号 5 次/5 分钟双重限流，计数存库、多实例共享。
5. 邮箱验证/修改与注册均带频率限制（注册：同邮箱每分钟最多 5 次；邮件发送：同邮箱每分钟 1 次）。
6. 封禁用户无法登录，也无法获取 JWT Token。
7. 头像端点做路径遍历防护；上传校验文件魔数签名；大于 512KB 自动压缩并生成 256px 缩略图。
8. JWT Token 支持黑名单撤销；同时校验 `sid` 指向的会话是否仍然有效，登出、踢出、封禁、改密后旧 Token 在数秒内失效。
9. 登录会话按设备独立保存（Refresh Token 有效期默认 30 天），支持多设备同时在线；同一「浏览器 + 操作系统」只保留最近一次登录，新登录会顶掉旧会话。
10. Refresh Token 每次刷新都轮换，旧值立即失效；检测到已用过的 Token 被再次提交时，判定为复用攻击并撤销整条会话。
11. 异常中间件在生产环境隐藏内部错误详情。
12. A 权限仅限「初始用户」组，不可分配给其他权限组；特殊权限（A/B/C）不可通过 API 创建、修改或删除。
13. 权限缓存采用全局版本号 + 版本前缀键，权限写操作只自增一次版本号，所有实例在约 1 秒内返回新结果；不再逐用户清缓存，批量改组内成员也不会产生 O(N) 写放大。
14. 自定义权限记录创建人（`creator_id`），系统内置权限创建人为 null。
15. 系统初始用户（uid=0）不可删除、不可修改、不可从初始权限组移除；启动时检查其归属，若不在则自动修复。
16. SMTP 连接默认加密（587=STARTTLS，465=SSL，其他端口默认 STARTTLS）。
17. 子系统 Secret 仅存 SHA256 哈希，明文只在创建/重置时返回一次；停用或删除后凭证立即失效。
18. 子系统凭证只能访问「仅需认证、无需用户权限」的端点（即权限检查接口）。
19. SMTP 密码经 Data Protection 加密存储，接口返回值脱敏（仅返回 `hasPassword`）。
20. 服务端日志不记录 SMTP 明文密码、子系统明文 Secret 与 JWT 明文令牌。
21. 登录审计（`login_logs`）记录事件、结果、IP、归属地、设备名与 UA，不记录密码与令牌明文；按 `Security:LoginHistoryRetentionDays` 定期清理。
22. 新设备或异地登录会异步发送邮件提醒（可在 `Security` 节关闭），登录接口不等待邮件发送结果。
23. 离线 IP 库仅做本地查询，不外发 IP；库文件缺失时归属地降级为「未知」，异地判定退回网段比对。

## 生产环境部署清单

> **环境变量只需一个：`UCS_DB_CONNECTION`。** 控制台的回环调用基地址自动取自进程真实监听地址，
> 不需要任何 SelfBaseUrl 相关环境变量；`appsettings.json` 中只需按模板填写 JWT Key、`AllowedOrigins`
> 等既有项。

- 通过环境变量 `UCS_DB_CONNECTION` 提供数据库连接串（唯一必需的环境变量）。
- 修改 JWT Key 为 64+ 字符的随机字符串。
- 配置 `AllowedOrigins` 限制跨域来源。
- 按需配置 `ApiBaseUrl`（仅用于验证邮件链接）为 HTTPS 地址。
- `SelfBaseUrl` 保持留空（自动解析，见第 6 节）。
- 配置反向代理（Nginx/Caddy）并启用 HTTPS。
- 如需登录归属地展示，把 `ip2region.xdb` 放入 `App_Data/`（随发布复制）；放不到也只影响归属地，不影响登录。
- 登录后立即修改管理员与初始用户口令。
- 在控制台「邮件服务」页面完成 SMTP 配置并发送测试邮件。
- 持久化 Data Protection 密钥环目录（`keys/`；容器化部署须挂载持久卷）。
- 反向代理须允许 WebSocket（转发 `Upgrade`/`Connection` 头）或关闭响应缓冲以支持 SSE 回退——管理前端交互依赖 `/_blazor` 长连接。
- 若代理不在本机，需在 `UseForwardedHeaders` 中登记代理地址（默认仅信任回环代理）。
- `AllowedHosts` 只登记对外域名即可，回环地址会自动并入白名单。

### 临时应急：回环地址解析异常

若上线后发现控制台提示「无法连接接口服务」且日志显示解析出的回环地址在本机不可达（例如容器内
`localhost` 不通、或监听地址被刻意收敛），可临时挂一个覆盖值恢复服务：

```ini
# systemd
Environment="AppSettings__SelfBaseUrl=https://your-public-domain"

# docker compose
environment:
  AppSettings__SelfBaseUrl: "https://your-public-domain"
```

该地址必须**从应用本机可访问**（通常就是对外域名）。修复或确认自动解析正常后应移除该变量。
若进程完全解析不到任何 HTTP/HTTPS 监听地址，应用会直接启动失败并在日志中提示配置该项。

## 日志系统

Serilog 结构化日志，输出三类文件：

```
logs/
├── access-YYYYMMDD.log    # HTTP 请求日志（保留 30 天）
├── error-YYYYMMDD.log     # 错误日志（保留 90 天）
└── app-YYYYMMDD.log       # 业务日志（保留 30 天）
```

控制台格式：`[HH:mm:ss LVL] Message`（仅 Information+）。日志级别通过 `appsettings.json` 的 `Serilog:MinimumLevel` 配置。

## 许可证

Apache License 2.0，参见 [LICENSE](LICENSE)。
