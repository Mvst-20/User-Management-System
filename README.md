# User Management System

基于 ASP.NET Core 8 Minimal API + MySQL 8.0 构建的用户管理系统。

## 功能特性

- **用户认证**：JWT Bearer Token 认证，支持 Refresh Token 免密刷新
- **密码安全**：BCrypt 哈希存储（Work Factor 12），防止暴力破解
- **邮箱验证**：完整的注册验证和邮箱修改流程，支持频率限制
- **头像管理**：支持上传、下载、删除用户头像（路径遍历防护 + 图片压缩 + 缩略图）
- **在线状态**：基于数据库的在线用户跟踪，多实例共享，自动过期清理
- **权限体系**：基于权限组的细粒度权限控制，支持 A/B/C 特殊权限
- **权限组管理**：支持创建、编辑、删除权限组及分配权限和成员
- **用户关注**：支持关注/取消关注、关注列表、粉丝列表、隐私设置
- **系统管理**：站点名称/LOGO 设置、友链管理、登录/注册开关
- **系统监控**：实时系统 CPU/内存/网络/运行时间监控
- **管理控制台**：内置 Blazor Web App（Interactive Server）+ Ant Design 管理前端，浏览器即可完成用户、权限组、权限、系统设置、邮件服务、子系统的日常运维；仪表盘含新增用户趋势折线图，服务器状态含 CPU/内存/网络历史折线图（指标 60s 落库、保留 8 天），并内置「运行日志」实时查看页
- **子系统凭证**：为接入的外部子系统签发 API Key 凭证（Secret 仅存 SHA256 哈希，明文仅展示一次），与用户 JWT 认证并存；子系统凭凭证**自注册权限**（强制「{标识}:」前缀），删除子系统时其注册的权限级联清理
- **邮件配置入库**：SMTP 配置存于数据库并经 Data Protection 加密，界面维护、修改即时生效，支持测试发信
- **后台服务**：自动清理过期 Token 和过期在线状态
- **自动初始化**：首次启动自动创建系统用户、管理员账户、权限和权限组
- **输入验证**：所有请求 DTO 使用 Data Annotations 验证
- **注册限流**：防止批量注册攻击
- **登录防护**：基于 IP + 账号的双重暴力破解防护（IP 20次/5分钟，账号 5次/5分钟），限流计数数据库存储、多实例共享
- **Token 撤销**：JWT Token 黑名单机制（数据库存储，支持多实例部署），登出和密码修改时自动撤销当前 Token
- **头像验证**：上传时验证文件魔数签名（magic bytes），防止 Content-Type 伪装；大于 512KB 自动压缩并生成缩略图
- **密码安全策略**：密码修改后强制下线并撤销当前 Token，需重新登录
- **异常处理**：统一异常中间件，生产环境隐藏内部错误信息

## 技术栈

- ASP.NET Core 8 Minimal API
- Blazor Web App（Interactive Server 渲染模式，内置管理控制台）
- Ant Design Blazor（管理界面组件库）
- Entity Framework Core 8 (Pomelo.EntityFrameworkCore.MySql)
- MySQL 8.0
- JWT Bearer Authentication（用户登录态）+ 子系统 API Key 认证方案
- ASP.NET Core Data Protection（SMTP 密码加密存储）
- MailKit (SMTP 邮件发送)
- BCrypt.Net-Next
- SixLabors.ImageSharp
- Serilog.AspNetCore (结构化日志)

## 快速开始

### 1. 配置数据库（系统变量）

数据库连接串**不写入配置文件**，通过系统（环境）变量提供。按操作系统选择对应写法。

#### Windows

**① 仅当前终端会话（临时，关闭终端即失效）**

```powershell
# PowerShell
$env:UCS_DB_CONNECTION="server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;"
```

```bat
:: 命令提示符（cmd）
set UCS_DB_CONNECTION=server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;
```

**② 永久生效（推荐）—— PowerShell 写入用户环境变量**

```powershell
[Environment]::SetEnvironmentVariable('UCS_DB_CONNECTION','server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;','User')
```

> 第三个参数 `'User'` 换成 `'Machine'` 即为系统级变量（需以管理员身份运行 PowerShell）。

**③ 图形界面方式**

「此电脑」右键 → 属性 → 高级系统设置 → 环境变量 → 在**用户变量**中新建：

| 项 | 内容 |
|----|------|
| 变量名 | `UCS_DB_CONNECTION` |
| 变量值 | `server=127.0.0.1;database=ucsdb;user=dbadmin;password=your_password;CharSet=utf8mb4;` |

> ⚠️ **变量值不要加引号**。从聊天窗口或文档复制时容易把引号一并带入（半角 `"` 或全角 `“ ”`）。
> 应用对此**不做静默修正**，而是直接拒绝并给出明确提示：
>
> ```
> UCS_DB_CONNECTION（环境变量） 的取值首尾包含引号（实际以「“」开头、以「”」结尾，长度 169）。
> 连接串必须是不带任何引号的纯文本，请去掉引号后重试。
> ```

**④ 验证变量是否生效**

```powershell
# 查看永久值（User 层）
[Environment]::GetEnvironmentVariable('UCS_DB_CONNECTION','User')

# 查看当前进程能否读到（须与运行应用的终端一致）
$env:UCS_DB_CONNECTION
```

> **设置永久变量后必须重新打开终端**（或重启 IDE / 应用）。已打开的终端与 Visual Studio 进程不会自动获得新变量，此时启动应用仍会报"未配置数据库连接串"。

#### Linux / macOS

```bash
export UCS_DB_CONNECTION="server=127.0.0.1;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;"
```

#### 容器 / 服务

```ini
# systemd 服务单元
Environment="UCS_DB_CONNECTION=server=127.0.0.1;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;"
```

```yaml
# docker compose
environment:
  UCS_DB_CONNECTION: "server=db;database=ucsdb;user=ucs;password=your_password;CharSet=utf8mb4;"
```

#### 解析优先级

| 优先级 | 来源 | 说明 |
|:---:|------|------|
| 1 | 环境变量 `UCS_DB_CONNECTION` | 推荐 |
| 2 | 配置键 `ConnectionStrings:Default` | 等价于环境变量 `ConnectionStrings__Default`（ASP.NET Core 标准约定） |

#### 启动失败（闪退）如何排查

启动失败时，应用会**同时**把原因输出到三处，不会出现"没有任何提示"：

| 位置 | 说明 |
|------|------|
| 控制台标准错误 | 完整失败报告（时间、工作目录、异常类型与信息、堆栈） |
| `logs/startup-error.log` | 同上，追加写入；不依赖日志框架，日志系统未初始化时依然有效 |
| `logs/error-YYYYMMDD.log` | Serilog 记录的错误与致命异常（含完整堆栈） |

> 上报过程**不阻塞**：不等待按键、不占用终端，服务与容器环境同样适用。

更直接的方式仍是**在终端中启动**（错误会留在窗口里）：

```powershell
cd e:\CodeBuddyProjects\UCS
dotnet run
```

> 两处都未提供连接串时，应用在启动阶段即失败，并列出**已检测到的相关变量名及所在层级**（User/Machine/Process），便于区分"没设置"与"设置了但当前进程读不到"。
>
> 启动成功后日志会打印目标与来源（**不含凭据**）：`Database: 127.0.0.1/ucsdb via env:UCS_DB_CONNECTION`。
>
> 连接字符串支持 MySQL 连接池参数：`MaximumPoolSize`（最大连接数）、`MinimumPoolSize`（最小连接数）、`ConnectionLifeTime`（连接寿命秒数）。
>
> 使用 `dotnet ef` 生成迁移时同样需要该环境变量（设计时会启动应用宿主）。

### 2. 配置 SMTP（管理界面维护）

SMTP 配置**不再从配置文件读取**。启动系统后登录管理控制台，进入「邮件服务」页面填写服务器地址、端口、SSL、账号、密码、发件人与显示名，保存即生效，并可直接发送测试邮件验证连通性。

> **配置文件中已移除 `Smtp` 段落**：邮件配置完全由数据库维护，`appsettings.json` 与 `appsettings.template.json` 均不再包含该段落，启动时也不会从配置文件导入。
>
> **端口规则保持不变**：587 自动 STARTTLS、465 自动 SSL 直连、其他端口按 `UseSsl` 决定。
>
> **Data Protection 密钥环**：SMTP 密码加密依赖密钥环，默认持久化到 `keys/` 目录（可用 `AppSettings:DataProtectionKeysPath` 指定）。密钥环丢失将导致已存密码无法解密，此时邮件服务会降级为"未配置"，需在界面重新录入密码。

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
    "AdminPasswordLength": 12,
    "SelfBaseUrl": "",
    "DataProtectionKeysPath": "keys"
  }
}
```

- `TokenExpiryMinutes`：邮箱验证 Token 的有效期（分钟），默认 20 分钟
- `AdminPasswordLength`：首次启动时自动生成的管理员和系统用户密码长度，默认 12 位
- `SelfBaseUrl`：管理控制台回环调用本进程 REST API 的基地址。**一般无需配置**——留空时按"浏览器实际访问的 scheme + host"解析，可自动适配反向代理与 `AllowedHosts` 限制；无请求上下文时（如启动日志）才从 `ASPNETCORE_URLS`/`urls` 推导（通配地址替换为 `localhost`），最终回落到 `http://localhost:5000`
- `DataProtectionKeysPath`：Data Protection 密钥环目录（SMTP 密码加密依赖），默认 `keys`，相对路径基于应用工作目录

> 仅当需要强制走特定内网地址（如 `http://127.0.0.1:5000`）时才配置 `SelfBaseUrl`。
> 该地址的 Host 会被自动加入 `HostFiltering` 白名单，因此即使 `AllowedHosts` 只登记了对外域名也能正常工作。

### 7. 运行

```bash
# 开发环境
dotnet run

# 生产环境
dotnet run --environment Production
```

**关于控制台窗口**：本项目是标准控制台应用，窗口是否出现取决于启动方式。

| 启动方式 | 独立窗口 |
|---------|---------|
| 双击 `bin\Debug\net8.0\UserManagementSystem.exe`（或发布目录中的同名 exe） | ✅ 出现，窗口随服务常驻 |
| 在已有终端中执行 `dotnet run` | ❌ 复用当前终端，不新开窗口 |
| `Start-Process` / `start` 命令 | ✅ 新开窗口 |
| Windows 服务 / 计划任务 / 容器 | ❌ 无窗口（设计如此） |

需要在新窗口中启动（便于持续观察日志）：

```powershell
Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory "e:\CodeBuddyProjects\UCS"
```

```bat
:: cmd
start "UCS" cmd /k "cd /d e:\CodeBuddyProjects\UCS && dotnet run"
```

**启动完成后**：宿主启动完毕才获知最终监听地址，届时打印（通配地址自动规范化为 `localhost`）：

```
  ──────────────────────────────────────────────
    管理控制台: http://localhost:5208/
    Swagger UI: http://localhost:5208/swagger
    Health:     http://localhost:5208/health
  ──────────────────────────────────────────────
```

> 端口来自 `Properties/launchSettings.json` 的 `applicationUrl`（默认 **5208**）；用 `--urls` 或 `ASPNETCORE_URLS` 启动则以实际值为准。日志在宿主启动后输出，因此**与真实端口一致**。

**Visual Studio 启动行为**（全部由 `Properties/launchSettings.json` 控制，与程序代码无关）：

| 配置 | 作用 |
|------|------|
| `launchBrowser: true` | 启动时自动打开浏览器 |
| 未设置 `launchUrl` | 打开**应用根路径**，即管理控制台 `/`（若设为 `swagger` 则打开接口文档） |
| `environmentVariables.UCS_DB_CONNECTION` | 开发环境连接串，避免依赖系统级变量（VS 不会继承启动后才新增的环境变量） |

> 程序代码中**没有任何**打开浏览器的逻辑。

首次启动时，系统会自动：
- 执行 EF Core 数据库迁移创建表结构（全新数据库直接建表；旧版 `EnsureCreated` 创建的已有数据库会自动建立迁移基线，无损升级）
- 初始化权限表（含 A/B/C 特殊权限和各 API 权限）
- 初始化权限组（初始用户、超级管理员、管理员、普通用户）
- 创建系统初始用户（uid=0，用户名: `system`，持有 A 权限）
- 生成超级管理员账户（用户名: `admin`，邮箱: `admin@system.local`，属于"超级管理员"组）
- 在控制台中显示随机生成的密码（⚠️ 请立即登录修改）

## 管理控制台（内置前端）

启动后访问 `http://<host>:<port>/` 即进入管理控制台，使用管理员账号登录（首次启动时控制台会打印随机密码）。

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
| 我的权限 | `/my-permissions` | 登录即可（右上角用户菜单进入） |

设计要点：

- **菜单与按钮按"有效权限"显隐**：登录后调用 `GET /api/users/me/permissions` 获取**展开后**的权限集合（含 A/B/C 特殊权限的覆盖效果），仅展示当前账号有权访问的菜单；实际鉴权仍由 API 端点的权限过滤器强制执行
- **前端不重复实现鉴权**：控制台通过服务端回环 HTTP 调用本进程 REST API（同一套端点与鉴权规则），因此既有的 59 个端点权限策略对控制台同样生效
- **会话保持**：Access Token 存于 circuit 内存并以受保护方式持久化到浏览器 sessionStorage，刷新页面不丢失登录态；过期前 60 秒自动刷新，收到 401 时自动跳转登录页
- **关键操作二次确认**：删除用户/权限组/权限/子系统、重置密码、重新生成子系统密钥均需确认；子系统 Secret 仅在创建或重置时展示一次
- **界面风格**：工业化冷淡风格——纯白底墨黑字、直角、1px 细线、无阴影无渐变；图标统一使用 Lucide 开源图标包（不自行绘制）。样式集中在 `wwwroot/app.css` 的 CSS 变量，详见 `docs/ADMIN_CONSOLE.md` 第 8 节
- **连接自检**：前端交互通道（`/_blazor`）建立失败时页面会明确提示，并在日志记录 `[UI] circuit established`，便于区分"通道未建立"与"应用逻辑异常"

详细使用说明见 [`docs/ADMIN_CONSOLE.md`](docs/ADMIN_CONSOLE.md)。

## 生产环境部署清单

### 必须完成

- [ ] 修改 JWT Key 为 64+ 字符的随机字符串
- [ ] 通过系统变量 `UCS_DB_CONNECTION` 提供数据库连接串（**不再写入配置文件**）
- [ ] 配置 `ApiBaseUrl` 为 HTTPS 地址
- [ ] 配置 `AllowedOrigins` 限制跨域来源
- [ ] 配置反向代理（Nginx/Caddy）并启用 HTTPS
- [ ] 登录后立即修改管理员密码
- [ ] 妥善保管系统初始用户（uid=0）密码
- [ ] 在管理控制台「邮件服务」页面完成 SMTP 配置并发送测试邮件
- [ ] 持久化 Data Protection 密钥环目录（`keys/`，容器化部署须挂载持久卷，否则重启后 SMTP 密码无法解密）
- [ ] **反向代理必须允许 WebSocket**（转发 `Upgrade`/`Connection` 头）或至少关闭响应缓冲以支持 SSE 回退——管理前端的交互依赖 `/_blazor` 长连接
- [ ] 若代理不在本机，需在 `Program.cs` 的 `UseForwardedHeaders` 中登记代理地址（默认仅信任回环代理）

## 权限体系

### 概述

系统采用**基于权限组的权限控制**模型：每个 API 端点对应一个权限代码，用户通过其所属的权限组获取权限。调用 API 时，系统检查用户所属权限组是否拥有该 API 对应的权限。

> 用户可同时属于多个权限组，最终权限为所有组权限的并集。系统初始用户（uid=0）始终保留在"初始用户"组中，不可移除。

### 特殊权限

| 权限 | 代码 | 说明 |
|------|------|------|
| A 权限 | `PERM_A` | 最高权限，可使用全站所有 API（包括 B、C 权限覆盖的 API）。仅"初始用户"组持有，不可分配给其他权限组 |
| B 权限 | `PERM_B` | 权限组管理权限，可管理权限表和权限组（除 ABC 外的所有权限） |
| C 权限 | `PERM_C` | 服务器配置管理权限，可使用服务器管理相关 API |

### 预设权限组

| 权限组 | 说明 | 成员锁定 | 可删除 | 基本用户权限 | 持有权限 |
|--------|------|----------|--------|--------------|---------|
| 初始用户 | 仅 uid=0 成员，持有 A 权限 | 是（不可添加/移除成员） | 否 | 否 | PERM_A |
| 超级管理员 | 除 A 外的所有权限 | 否 | 否 | 否 | PERM_B, PERM_C + 全部 API 权限（含关注、系统设置、邮件配置、子系统） |
| 管理员 | 用户管理和基本管理权限 | 否 | 是 | 否 | 用户管理（含重置密码）+ 统计 + 头像 + 权限组查看 + 登出 + 关注 + 系统设置 |
| 普通用户 | 基本用户权限 | 否 | 是 | **是** | 查看/更新自己 + 修改密码/邮箱 + 头像 + 登出 + 关注 |

> **初始权限组**：不可编辑（名称、描述、权限均不可修改）、不可删除、不可添加或移除成员。
>
> **超级管理员组**：不可删除，但可修改成员和权限。

### 权限代码列表

| 分类 | 代码 | 名称 | 说明 |
|------|------|------|------|
| special | `PERM_A` | 最高权限 | 全站所有 API |
| special | `PERM_B` | 权限管理权限 | 管理权限表和权限组（除 ABC） |
| special | `PERM_C` | 服务器管理权限 | 服务器管理 API |
| user | `user:read` | 查看用户 | 查看用户信息 |
| user | `user:create` | 创建用户 | 管理员直接创建用户（可跳过邮箱验证） |
| user | `user:update` | 更新用户 | 更新用户信息 |
| user | `user:delete` | 删除用户 | 删除用户 |
| user | `user:list` | 用户列表 | 查看用户列表 |
| user | `user:change-password` | 修改密码 | 修改用户密码 |
| user | `user:change-email` | 修改邮箱 | 修改用户邮箱 |
| user | `user:logout` | 登出 | 用户登出 |
| user | `user:reset-password` | 重置密码 | 管理员重置指定用户的登录密码 |
| avatar | `avatar:upload` | 上传头像 | 上传用户头像 |
| avatar | `avatar:delete` | 删除头像 | 删除用户头像 |
| admin | `admin:stats` | 查看统计 | 管理员统计数据（C 权限覆盖） |
| admin | `admin:server` | 服务器管理 | 服务器状态和配置（C 权限覆盖） |
| permission | `permission:read` | 查看权限 | 查看权限列表（B 权限覆盖） |
| permission | `permission:create` | 创建权限 | 创建新权限（B 权限覆盖） |
| permission | `permission:update` | 更新权限 | 更新权限信息（B 权限覆盖） |
| permission | `permission:delete` | 删除权限 | 删除权限（B 权限覆盖） |
| user-group | `user-group:read` | 查看权限组 | 查看权限组（B 权限覆盖） |
| user-group | `user-group:create` | 创建权限组 | 创建新权限组（B 权限覆盖） |
| user-group | `user-group:update` | 更新权限组 | 更新权限组信息（B 权限覆盖） |
| user-group | `user-group:delete` | 删除权限组 | 删除权限组（B 权限覆盖） |
| user-group | `user-group:manage-members` | 管理成员 | 添加/移除权限组成员（B 权限覆盖） |
| user-group | `user-group:manage-permissions` | 管理权限 | 设置权限组权限（B 权限覆盖） |
| follow | `follow:user` | 关注用户 | 关注和取消关注其他用户 |
| follow | `follow:read` | 查看关注 | 查看关注列表、粉丝列表和关注统计 |
| follow | `follow:manage` | 管理关注设置 | 管理关注列表隐私设置 |
| system-setting | `system-setting:read` | 查看系统设置 | 查看系统设置信息 |
| system-setting | `system-setting:update` | 更新系统设置 | 更新站点名称、LOGO、友链、登录/注册开关 |
| smtp | `smtp:read` | 查看邮件配置 | 查看邮件服务配置（密码脱敏） |
| smtp | `smtp:update` | 管理邮件配置 | 修改邮件服务配置并发送测试邮件 |
| subsystem | `subsystem:read` | 查看子系统 | 查看接入的子系统与凭证信息 |
| subsystem | `subsystem:create` | 创建子系统 | 登记子系统并签发访问凭证 |
| subsystem | `subsystem:update` | 管理子系统 | 更新子系统信息、启停、重置密钥 |
| subsystem | `subsystem:delete` | 删除子系统 | 删除子系统并使其凭证立即失效 |

### 权限检查逻辑

1. 用户请求 API → 从 JWT 获取用户 ID
2. 查询用户所属的所有权限组及其权限（带内存缓存，5 分钟过期）
3. 检查顺序：
   - 拥有 **A 权限** → 直接通过
   - API 属于 B 权限覆盖范围（权限管理 + 权限组管理）且 拥有 **B 权限** → 通过
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
| GET | `/api/system/info` | 获取系统基本信息（站点名/LOGO/登录注册开关） | - |
| GET | `/api/system/friend-links` | 获取友情链接 | - |
| GET | `/health` | 健康检查 | - |

> 项目内置 Swagger UI（仅开发环境），启动后访问 `/swagger` 可在线调试所有接口。

**请求验证规则**：
- `username`：3-64 个字符
- `email`：有效邮箱格式，最大 128 字符
- `password`：8-128 个字符

**注册限流**：同一邮箱每分钟最多 5 次注册请求。

> 新注册用户会自动加入所有开启了**「基本用户权限」**（`is_default_for_new_users = true`）的权限组。内置的"普通用户"组默认开启该标记；若管理员关闭所有组的该标记，则新用户不加入任何权限组。

### 用户端点（需要认证 + 权限）

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
| POST | `/api/users/logout` | 用户登出 | `user:logout` | 否 |

> **allowSelf**：访问自己的资源（路由参数 `{id}` 与当前用户 ID 匹配）时跳过权限检查。change-password 和 change-email 不使用 allowSelf，而是始终要求权限并手动校验仅限本人操作。

> **邮箱修改**：普通用户不能通过 PUT/PATCH 直接修改邮箱，必须通过 change-email 流程（验证密码 → 发送验证邮件 → 确认修改）。管理员可直接通过 PUT 修改。

> **手机号**：仅在 PUT/PATCH 更新时支持，最大 20 字符。注册时暂不支持。

> **头像上传**：仅支持 JPG/PNG/GIF/WebP 格式，最大 6MB。文件大于 512KB 时自动压缩并生成缩略图（256px），小于等于 512KB 时不压缩（缩略图 = 原图）。获取头像时可通过 `?type=thumb` 参数获取缩略图。

### 关注端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| POST | `/api/users/{userId}/follow` | 关注用户 | `follow:user` |
| DELETE | `/api/users/{userId}/follow` | 取消关注 | `follow:user` |
| GET | `/api/users/{userId}/follow/status` | 检查是否已关注 | `follow:read` |
| GET | `/api/users/me/following` | 获取我的关注列表 | `follow:read` |
| GET | `/api/users/me/followers` | 获取我的粉丝列表 | `follow:read` |
| GET | `/api/users/me/follow/stats` | 获取我的关注统计 | `follow:read` |
| GET | `/api/users/{userId}/following` | 获取指定用户关注列表（受隐私设置影响） | `follow:read` |
| GET | `/api/users/{userId}/followers` | 获取指定用户粉丝列表 | `follow:read` |
| GET | `/api/users/{userId}/follow/stats` | 获取指定用户关注统计 | `follow:read` |
| PUT | `/api/users/me/following/visibility` | 设置关注列表可见性 | `follow:manage` |
| GET | `/api/users/me/following/visibility` | 获取关注列表可见性设置 | `follow:manage` |

> **隐私设置**：用户可设置关注列表仅自己可见（`PUT /api/users/me/following/visibility`）。隐藏后，他人查看该用户关注列表时返回空列表。
>
> **分页参数**：列表接口支持 `page`（默认 1）和 `pageSize`（默认 20，最大 100）Query 参数。

### 管理员端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 | 特殊覆盖 |
|------|------|------|---------|---------|
| GET | `/api/admin/stats/total-users` | 总用户数量（排除已删除） | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/online-users` | 在线用户数量 | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/new-users` | 新注册用户数 | `admin:stats` | C 权限 |
| GET | `/api/admin/stats/active-users` | 活跃用户数 | `admin:stats` | C 权限 |
| GET | `/api/admin/server/status` | 服务器状态 | `admin:server` | C 权限 |
| POST | `/api/admin/email/test` | 发送测试邮件（使用数据库中的 SMTP 配置） | `admin:server` | C 权限 |
| POST | `/api/admin/users` | 管理员创建用户（不发送验证邮件；密码留空则生成） | `user:create` | - |
| POST | `/api/admin/users/{id}/reset-password` | 重置指定用户密码 | `user:reset-password` | - |

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

> B 权限持有者可管理权限表和权限组，但**不可**创建、修改或删除 A/B/C 特殊权限。

### 权限组管理端点（需要认证 + 权限）

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

> **初始权限组**（`is_protected=true, is_member_locked=true`）不可编辑、不可删除、不可添加/移除成员。
>
> **超级管理员组**（`is_protected=true`）不可删除，但可修改成员和权限。
>
> **A 权限不可分配**给除"初始用户"外的任何权限组。
>
> **基本用户权限**（`is_default_for_new_users`，布尔，默认 `false`）：为 `true` 时，**新注册用户将默认加入该权限组**。可同时为多个组开启；成员锁定的组（初始用户）即使开启也不会被自动分配。内置的"普通用户"组默认开启。

### 系统管理端点

#### 公开接口（无需认证）

| 方法 | 路由 | 描述 |
|------|------|------|
| GET | `/api/system/info` | 获取系统基本信息（站点名、LOGO URL、登录/注册开关） |
| GET | `/api/system/friend-links` | 获取友情链接列表 |

#### 管理接口（需要认证 + `system-setting` 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| PUT | `/api/admin/system/info` | 更新系统基本信息 | `system-setting:update` |
| PUT | `/api/admin/system/friend-links` | 更新友情链接 | `system-setting:update` |
| GET | `/api/admin/system/settings` | 获取所有系统设置 | `system-setting:read` |
| GET | `/api/admin/system/settings/{key}` | 获取单个设置 | `system-setting:read` |
| PUT | `/api/admin/system/settings/{key}` | 更新单个设置 | `system-setting:update` |

> **系统信息**：`site_name`（站点名称）、`logo_url`（LOGO URL）、`allow_login`（是否允许登录，true/false）、`allow_register`（是否允许注册，true/false）。默认为允许。
>
> **友情链接**：JSON 数组格式存储，每项包含 `name`、`url`、`description`（可选）、`logoUrl`（可选）。

### 邮件服务配置端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/admin/smtp/config` | 获取邮件服务配置（密码脱敏，仅返回 `hasPassword`） | `smtp:read` |
| PUT | `/api/admin/smtp/config` | 保存邮件服务配置（密码留空表示不修改） | `smtp:update` |
| POST | `/api/admin/smtp/test` | 使用当前配置发送测试邮件 | `smtp:update` |

> 配置存于数据库 `smtp_config` 单行表，密码经 Data Protection 加密；保存后立即生效（缓存自动失效）。
>
> 启用邮件服务时 `host`、`port`、`fromAddress` 为必填，否则返回 `code=2`。

### 子系统管理端点（需要认证 + 权限）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/subsystems` | 子系统列表（分页 + 关键字搜索） | `subsystem:read` |
| GET | `/api/subsystems/{id}` | 子系统详情 | `subsystem:read` |
| POST | `/api/subsystems` | 登记子系统并签发凭证 | `subsystem:create` |
| PUT | `/api/subsystems/{id}` | 更新子系统信息（不影响凭证） | `subsystem:update` |
| PUT | `/api/subsystems/{id}/status` | 启用/停用子系统 | `subsystem:update` |
| POST | `/api/subsystems/{id}/regenerate-secret` | 重新生成凭证（旧凭证立即失效） | `subsystem:update` |
| DELETE | `/api/subsystems/{id}` | 删除子系统 | `subsystem:delete` |

> **凭证策略**：`apiKey` 明文存储用于定位与展示；`apiSecret` 仅存 SHA256 哈希，**明文只在创建或重置时返回一次**。
>
> **子系统调用方式**：请求头携带 `X-Api-Key` + `X-Api-Secret`，可访问"仅需认证、无需用户权限"的端点（即 `POST /api/permissions/check`，且必须显式传入 `userId`）。详见接入指南。

### 管理前端支撑端点（需要认证）

| 方法 | 路由 | 描述 | 所需权限 |
|------|------|------|---------|
| GET | `/api/users/me/permissions` | 获取当前用户**展开后**的有效权限码集合 | 认证即可 |
| POST | `/api/admin/users/{id}/reset-password` | 重置指定用户密码（不传新密码则服务端生成并一次性返回） | `user:reset-password` |

> `GET /api/users/me/permissions` 返回 `{ userId, codes: [...] }`，`codes` 已包含 A/B/C 特殊权限的覆盖展开结果，供管理前端控制菜单与按钮显隐。

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
| 6xx | 用户操作 | 600=获取成功, 601=用户不存在, 610=更新成功, 611=用户不存在, 612=禁止修改, 613=用户名已存在, 614=邮箱已使用, 615=手机号已使用, 616=状态限制, 617=权限级别不足, 620=删除成功, 621=用户不存在, 630=列表成功 |
| 7xx | 头像 | 700=上传成功, 701=格式错误, 702=文件过大, 703=禁止上传, 710=删除成功, 711=头像不存在 |
| 8xx | 管理员 | 800=统计成功, 810=服务器状态成功 |
| 9xx | 邮件 | 900=发送成功, 901=发送失败 |
| 10xx | 权限 | 1000=权限不足, 1001=权限不存在, 1002=权限代码已存在, 1010=创建成功, 1011=更新成功, 1012=删除成功, 1013=不可修改特殊权限 |
| 11xx | 权限组 | 1100=权限组不存在, 1101=名称已存在, 1110=创建成功, 1111=更新成功, 1112=删除成功, 1113=受保护不可操作, 1120=成员已存在, 1121=成员不存在, 1122=添加成功, 1123=移除成功, 1124=成员锁定, 1130=设置权限成功, 1131=A权限限制, 1150=权限检查完成 |

## 用户状态

| 值 | 名称 | 说明 |
|----|------|------|
| 1 | Normal | 正常用户，完整功能 |
| 2 | Banned | 被封禁，无法登录 |
| 3 | Unverified | 待验证，已注册但未验证邮箱 |
| 4 | Deleted | 软删除，账号已注销 |

## 权限组级别（Level）

每个权限组都有 `level` 字段，**数值越小权限越高**：

| 权限组 | Level | 说明 |
|--------|-------|------|
| 初始用户 | **0** | 最高级别，不可修改 |
| 超级管理员 | **2** | |
| 管理员 | **10** | |
| 普通用户 | **20** | |
| 新建权限组 | **100** | 默认最低 |
| 无权限组用户 | `int.MaxValue` | 视为最低权限 |

**判定规则**：

- 操作者级别 = 其所属权限组中**最小**的 level；目标用户同理
- **目标级别 ≤ 操作者级别 → 拒绝**（`617`）：不能管理同级或更高级的用户
- 分配 / 新建 / 编辑 / 删除权限组时，目标组级别必须**严格大于**操作者级别（数值更大），禁止同级与更高级
- 添加 / 移除权限组成员时，目标用户的级别必须**严格大于**操作者级别；因此**移除自己**也等同"管理同级用户"而被拒绝
- 初始用户组（level 0）的级别不可修改

**覆盖范围**：`PUT/PATCH /api/users/{id}`、`PUT /api/users/{id}/groups`、`POST /api/admin/users/{id}/reset-password`、`DELETE /api/users/{id}`，权限组的创建 / 更新 / 删除，以及权限组成员的添加 / 移除（`POST`/`DELETE /api/user-groups/{id}/members[/{userId}]`）。操作自己的**资料**不受级别限制（状态等字段另有单独规则），但**组成员操作不豁免**。

## 数据库表

| 表名 | 说明 |
|------|------|
| `users` | 用户表（含自定义 JSON 扩展字段 `extras`、Refresh Token、关注列表隐私设置） |
| `email_verification_tokens` | 邮箱验证令牌表 |
| `permissions` | 权限表（含特殊权限 A/B/C 和各 API 权限，`creator_id` 记录创建人，`subsystem_id` 标记归属子系统） |
| `user_groups` | 权限组表（含 `level` 权限级别、`is_protected` 受保护、`is_member_locked` 成员锁定、`is_default_for_new_users` 基本用户权限） |
| `user_group_members` | 权限组成员关联表 |
| `user_group_permissions` | 权限组权限关联表 |
| `user_follows` | 用户关注关系表（follower_id + following_id 唯一约束） |
| `system_settings` | 系统设置表（键值对结构，支持 string/bool/json 类型） |
| `revoked_tokens` | JWT 黑名单表（已撤销 Token 的 jti，按过期时间自动清理，多实例共享） |
| `rate_limit_entries` | 限流计数表（原子 upsert 递增，多实例共享限流窗口） |
| `online_users` | 在线用户活跃记录表（30 分钟无活动视为离线，自动清理） |
| `smtp_config` | 邮件服务配置表（单行，主键固定为 1；密码经 Data Protection 加密存储） |
| `subsystems` | 子系统凭证表（`code`/`api_key` 唯一索引；`api_secret_hash` 仅存 SHA256 哈希） |

> 表结构由 EF Core Migrations 管理（见 `Migrations/` 目录），服务启动时自动应用迁移。

## 项目结构

```
UCS/
├── Auth/               # 管理前端认证与授权状态
│   ├── AuthSchemes.cs                       # 认证方案名（Bearer / SubsystemKey / MultiAuth）
│   ├── TokenStore.cs                        # circuit 级会话状态
│   ├── SessionPersistence.cs                # 会话的浏览器侧受保护持久化
│   ├── JwtAuthenticationStateProvider.cs    # 认证状态提供者（含刷新恢复）
│   └── PermissionState.cs                   # 有效权限集合缓存
├── Components/         # Blazor 管理控制台（Interactive Server）
│   ├── App.razor                            # 根组件（HTML 骨架、资源注册、连接诊断提示）
│   ├── Routes.razor                         # 路由表（AuthorizeRouteView）
│   ├── _Imports.razor                       # 全局 using
│   ├── Layout/                              # MainLayout / NavMenu / TopBar / EmptyLayout
│   ├── Shared/                              # PageContainer / PermissionGate / RedirectToLogin
│   │                                        # UiIcon（Lucide 图标组件）/ UiIconData（官方图标数据）
│   └── Pages/                               # 登录/仪表盘/服务器/用户/权限组/权限/设置/邮件/子系统/403
├── Configuration/       # 配置类 (AppConfiguration, JwtConfig, SmtpConfig, AppSettings)
│                        # DatabaseConnection：连接串解析（系统变量优先）
├── Data/               # 数据库上下文 (ApplicationDbContext) 与迁移执行器 (DatabaseMigrator)
├── DTOs/               # 数据传输对象
│   ├── ApiResponse.cs              # API 统一响应格式
│   ├── ResultCodes.cs              # 响应状态码常量
│   ├── PermissionCodes.cs          # 权限代码常量
│   ├── PermissionDTOs.cs           # 权限和权限组 DTO
│   ├── UserDTOs.cs                 # 用户相关 DTO
│   ├── FollowDTOs.cs               # 关注相关 DTO
│   └── SystemSettingDTOs.cs        # 系统设置相关 DTO
├── Endpoints/          # API 端点定义（按业务域垂直切片）
│   ├── AuthEndpoints.cs           # 认证端点（注册/登录/刷新/邮箱验证/登出）
│   ├── UserProfileEndpoints.cs    # 用户资料端点（查询/更新/删除/权限组/密码/邮箱）
│   ├── AdminEndpoints.cs          # 管理员端点（统计/服务器状态）
│   ├── AvatarEndpoints.cs         # 头像端点（上传/下载/删除）
│   ├── PermissionEndpoints.cs     # 权限管理端点（含单码/批量权限检查）
│   ├── UserGroupEndpoints.cs      # 权限组管理端点
│   ├── FollowEndpoints.cs         # 关注管理端点
│   ├── SystemSettingEndpoints.cs  # 系统设置端点
│   ├── SmtpConfigEndpoints.cs     # 邮件服务配置端点（读取/保存/测试发信）
│   └── SubsystemEndpoints.cs      # 子系统管理端点（签发/重置凭证/启停）
├── Exceptions/         # 业务异常 (BusinessException：服务层显式业务错误)
├── Extensions/         # 扩展方法 (UserExtensions, PermissionEndpointExtensions,
│   │                    ServiceCollectionExtensions, WebApplicationExtensions)
├── Logging/            # 日志 (SerilogConfig, LoggingCircuitHandler)
├── Middleware/         # 中间件 (ExceptionHandlingMiddleware)
├── Migrations/         # EF Core 数据库迁移（启动时自动应用，支持旧库基线兼容）
├── Models/             # 实体模型
│   ├── User.cs                     # 用户模型
│   ├── EmailVerificationToken.cs   # 邮箱验证令牌
│   ├── Permission.cs               # 权限
│   ├── UserGroup.cs                # 权限组
│   ├── UserGroupMember.cs          # 权限组成员
│   ├── UserGroupPermission.cs      # 权限组权限
│   ├── UserFollow.cs               # 用户关注关系
│   ├── SystemSetting.cs            # 系统设置
│   ├── RevokedToken.cs             # JWT 黑名单（撤销的 Token）
│   ├── RateLimitEntry.cs           # 限流计数条目
│   ├── OnlineUser.cs               # 在线用户活跃记录
│   ├── SmtpConfig.cs               # 邮件服务配置（单行 + 密码密文）
│   └── Subsystem.cs                # 子系统凭证（api_key 可查 / secret 仅存哈希）
├── docs/               # 文档
│   ├── INTEGRATION_GUIDE.md        # 业务系统接入指南
│   └── ADMIN_CONSOLE.md            # 管理控制台使用说明
├── Services/           # 业务服务
│   ├── ConsoleService.cs          # 控制台输出
│   ├── EmailService.cs            # 邮件发送
│   ├── FollowService.cs           # 关注管理
│   ├── InitializationService.cs   # 启动初始化（含权限和权限组种子数据，异常降级运行）
│   ├── JwtService.cs              # JWT Token 生成
│   ├── OnlineUserService.cs       # 在线用户跟踪（数据库存储，多实例共享）
│   ├── PermissionService.cs       # 权限检查（ABC 覆盖）和权限 CRUD（含种子数据）
│   ├── RateLimitService.cs        # 限流计数服务（数据库原子 upsert，多实例共享）
│   ├── RefreshTokenService.cs     # Refresh Token 管理（轮换防重放）
│   ├── SystemMonitorService.cs    # 系统资源监控
│   ├── SystemSettingService.cs    # 系统设置管理（含缓存）
│   ├── TokenBlacklistService.cs   # JWT Token 黑名单（数据库存储 + 内存命中缓存）
│   ├── TokenCleanupService.cs     # 后台清理服务（每小时：过期 Token/黑名单/限流/在线记录）
│   ├── TokenService.cs            # 验证 Token 管理
│   ├── UserGroupService.cs        # 权限组域服务（组 CRUD/成员管理/组权限分配）
│   ├── UserService.cs             # 用户业务逻辑
│   └── Api/                       # 管理前端 API 访问层
│       ├── UcsApiClient.cs                  # 调用本进程 REST API（拆包/注入令牌/自动刷新/401 处理）
│       ├── SelfBaseUrlProvider.cs            # 回环调用基地址解析（按请求来源，兼容严格 AllowedHosts）
│       ├── ApiResult.cs                     # 调用结果与分页结果封装
│       └── EndpointDataModels.cs            # 服务端匿名对象响应的前端模型
├── wwwroot/
│   ├── avatars/        # 头像存储
│   └── app.css         # 管理控制台全局样式与设计令牌
├── logs/               # Serilog 结构化日志（access/error/app 每日滚动）
├── appsettings.json    # 应用配置
├── LICENSE             # Apache License 2.0
└── Program.cs          # 入口点
```

## 安全说明

1. 首次运行后请立即登录并修改管理员密码
2. 初始管理员密码和系统用户密码仅输出到控制台一次，**不会持久化到文件**
3. **生产环境请务必修改 JWT Key**（至少 64 字符的随机字符串）
4. **生产环境请务必修改数据库密码**
5. 生产环境请配置 `AllowedOrigins` 限制跨域来源
6. 生产环境请配置 `ApiBaseUrl` 为 HTTPS 地址
7. 密码使用 BCrypt Work Factor 12 存储
8. 邮件发送频率限制：同一邮箱 1 分钟内只能请求一次
9. 注册频率限制：同一邮箱每分钟最多 5 次
10. 封禁用户无法登录，也无法获取 JWT Token
11. 头像端点已做路径遍历防护
12. 头像上传验证文件魔数签名（magic bytes），防止 Content-Type 伪装
13. 头像大于 512KB 时自动压缩，生成 256px 缩略图，减少存储和传输开销
14. 登录暴力破解防护：IP 20次/5分钟 + 账号 5次/5分钟双重限流
15. JWT Token 支持黑名单撤销，登出和密码修改时自动撤销当前 Token 和 Refresh Token
16. 支持 Refresh Token 机制（30 天有效），免密刷新 Access Token
17. 异常中间件在生产环境隐藏内部错误详情
18. A 权限仅限"初始用户"组，无法分配给其他权限组
19. 特殊权限（A/B/C）不可通过 API 创建、修改或删除
20. 权限检查结果带内存缓存（5 分钟过期），修改权限后自动清除对应用户缓存
21. 自定义权限记录创建人（`creator_id`），系统内置权限创建人为 null
22. 系统初始用户（uid=0）不可删除、不可修改、不可从初始权限组移除
23. 启动时自动检查 uid=0 是否在初始权限组，若不在则自动修复
24. SMTP 连接默认使用加密（端口 587=STARTTLS，465=SSL，其他端口默认 STARTTLS）
25. 管理控制台通过服务端回环调用本进程 REST API，鉴权规则与外部接入方完全一致，不存在独立后门
26. 子系统 Secret 仅存 SHA256 哈希，明文只在创建/重置时返回一次；停用或删除子系统后凭证立即失效
27. 子系统凭证只能访问"仅需认证、无需用户权限"的端点（即权限检查接口），无法调用需要用户权限的端点
28. SMTP 密码经 Data Protection 加密存储，接口返回值一律脱敏（仅返回 `hasPassword`）
29. 服务端日志不记录 SMTP 明文密码、子系统明文 Secret 与 JWT 明文令牌

## 日志系统

使用 Serilog 结构化日志，输出三类文件：

```
logs/
├── access-YYYYMMDD.log    # HTTP请求日志（保留30天）
├── error-YYYYMMDD.log     # 错误日志（保留90天）
└── app-YYYYMMDD.log       # 业务日志（保留30天）
```

控制台输出格式：`[HH:mm:ss LVL] Message`（仅 Information+）。通过 `appsettings.json` 中的 `Serilog:MinimumLevel` 配置日志级别。

## 许可证

Apache License 2.0 - 参见 [LICENSE](LICENSE) 文件
