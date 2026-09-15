# UCS 管理控制台使用说明

> 版本：v1.1.0 | 最后更新：2026-09-15
>
> 本文档介绍 UCS 内置管理控制台的使用方法。若需了解**业务系统如何调用 UCS 接口**，请阅读 [INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md)。

---

## 1. 概览

UCS 内置一套 Blazor 管理控制台（Interactive Server 渲染 + Ant Design Blazor），与 REST API 运行在**同一个进程**中，无需额外部署前端服务。

| 项目 | 说明 |
|------|------|
| 访问地址 | `http://<host>:<port>/`（VS / `dotnet run` 走 `http` 配置时为 `http://localhost:5208/`；程序启动日志会打印实际地址） |
| 渲染模式 | Blazor Web App / Interactive Server（服务端交互，通过 SignalR 长连接） |
| 认证方式 | 用户 JWT（登录后由服务端持有，受保护持久化到浏览器 sessionStorage） |
| 数据访问 | 服务端回环 HTTP 调用本进程 REST API（同一套端点与鉴权规则） |
| 首次登录 | 使用初始化时控制台打印的 `admin` 账号与随机密码 |

> 管理控制台**没有独立的鉴权后门**：它调用的就是对外暴露的 REST API，因此你在界面上能做什么，完全取决于账号的权限。

---

## 2. 登录与登出

### 2.1 登录

1. 浏览器访问根路径 `/`，未登录会自动跳转到 `/login`
2. 输入**用户名或邮箱**与密码（支持两者其一）
3. 登录成功后自动加载"有效权限"，并跳转到仪表盘；若因权限不足被拦截至某页面，会原路返回（`returnUrl`）

失败提示：

| 提示 | 原因 |
|------|------|
| 用户名或密码错误 | 凭证不正确 |
| 账号未验证，请先验证邮箱 | `status=3`（未验证） |
| 账号已被封禁 | `status=2` |
| 系统暂未开放登录 | 站点设置中关闭了登录开关 |
| 登录尝试过于频繁，请稍后再试 | 触发限流（同 IP 5 分钟 20 次 / 同账号 5 分钟 5 次） |

> **限流说明**：仅在登录失败时累积计数，登录成功会立即清零。

### 2.2 会话保持

- Access Token 存于服务端 circuit 内存，并以受保护方式写入浏览器 `sessionStorage`，**刷新页面不会丢失登录态**
- 令牌在过期前 60 秒自动刷新，无需手动操作
- 收到 401（令牌被撤销/失效）会自动清理会话并跳回登录页，提示"登录状态已失效"
- **关闭浏览器标签页后会话失效**（sessionStorage 按标签页隔离），需重新登录

### 2.3 登出

点击右上角用户菜单 → 「退出登录」。登出会调用 `/api/users/logout`，撤销 Refresh Token 并将当前 Access Token 加入黑名单，同时把用户标记为离线。

> 需具备 `user:logout` 权限。

---

## 3. 界面布局

```
┌──────────────┬────────────────────────────────────────────┐
│              │  顶部栏：站点名 · 当前用户 · 退出登录         │
│  侧边导航栏   ├────────────────────────────────────────────┤
│  （按权限     │                                            │
│   显隐）      │            页面内容区                       │
│              │   （页头 + 操作区 + 卡片/表格/弹窗）           │
│  收起/展开    │                                            │
└──────────────┴────────────────────────────────────────────┘
```

- **侧边导航**：按「总览 / 用户与权限 / 系统」分区，**仅显示当前账号有权访问的菜单项**；可收起为图标模式
- **顶部栏**：站点名、当前用户名与退出入口
- **内容区**：统一「页头（标题+副标题）+ 操作按钮区 + 内容卡片」结构

---

## 4. 功能页面

### 4.1 仪表盘（`/`）

页面由三部分组成：**指标卡片**、**趋势折线图**（横向并排，窄屏自动堆叠）、**快速开始侧栏**。

**指标卡片**：

| 指标 | 数据来源 | 说明 |
|------|---------|------|
| 总用户数 | `/api/admin/stats/total-users` | 排除已软删除用户 |
| 当前在线 | `/api/admin/stats/online-users` | 30 分钟内有活动的用户 |
| 近 N 天新增 | `/api/admin/stats/new-users?days=N` | 支持 7 / 30 / 90 天切换 |
| 近 N 天活跃 | `/api/admin/stats/active-users?days=N` | 统计区间内登录过的用户 |

**趋势折线图**（均取自 `/api/admin/stats/new-users/daily?days=N`，可切换 7 / 30 / 90 天）：

| 图表 | 曲线 | 说明 |
|------|------|------|
| 用户规模趋势 | 总用户数 / 新增用户 / 注销用户 | 总用户数为截至当日的累计值；注销用户以红色区分 |
| 活跃与在线趋势 | 活跃用户 / 当前在线 | 活跃为当日登录近似值；当前在线为快照横线（绿色） |

> 需要 `admin:stats` 权限（`PERM_C` 覆盖）。无权限时页面会提示所需权限。

### 4.2 服务器状态（`/server`）

- 运行时长、版本号
- 数据库连通状态（正常 / 异常）
- CPU 使用率、内存占用（含进度条与实际用量）
- 网络发送/接收速率与累计流量
- **历史趋势折线图**：CPU / 内存 / 网络速率随时间变化，数据来自 `/api/admin/server/metrics?hours=N`（支持 6 / 24 / 72 / 168 小时）
- **自动刷新**：默认每 10 秒刷新一次，可手动暂停；离开页面自动停止计时器

> 需要 `admin:server` 权限（`PERM_C` 覆盖）。

### 4.3 用户管理（`/users`）

**列表**：分页展示 ID、用户名、邮箱、手机号、状态、最后登录、注册时间

**筛选**：关键词（用户名/邮箱）+ 状态（正常 / 封禁 / 未验证 / 已删除，默认不含已删除）

**操作**（每项按权限独立显隐）：

| 操作 | 所需权限 | 说明 |
|------|---------|------|
| 添加用户 | `user:create` | 弹窗填写用户名、邮箱、密码（留空则生成并一次性展示）、手机号、是否需邮箱验证、初始权限组 |
| 编辑 | `user:update` | 覆盖用户表全部可编辑字段（见下） |
| 权限组 | `user-group:manage-members` | 勾选后**整体替换**该用户的权限组 |
| 重置密码 | `user:reset-password` | 留空则服务端生成随机密码并**一次性展示**；重置后目标用户被强制下线 |
| 删除 | `user:delete` | 软删除（`status=4`），二次确认 |

**编辑弹窗字段**（对应 `PUT /api/users/{id}`）：

| 字段 | 说明 |
|------|------|
| 用户名 / 邮箱 / 手机号 | 邮箱可供管理员为他人修改；修改后该邮箱的验证状态被清空 |
| 状态 | 正常 / 封禁 / 未验证；可借此**解封**已封禁账号 |
| 头像 | 上传（≤6MB，jpg/png/gif/webp）或删除，需 `avatar:upload` / `avatar:delete`；上传后立即刷新预览与列表 |
| 扩展字段 | JSON 文本，**留空表示不修改** |
| 关注列表可见性 | 是否隐藏该用户的关注列表 |

**权限组分配弹窗**：受保护权限组（成员锁定，如"初始用户"）**不会出现在可分配列表中**；若用户已是其成员，则以不可取消勾选的形式只读展示。

> **操作失败会直接显示在弹窗内部**（此前错误只渲染在弹窗背后的页面上，容易被误认为"点击确认没有任何反应"）。
>
> **权限级别限制**：不能编辑权限级别**不低于自己**的用户（同级与更高级一律拒绝，服务端返回 `617`）；对应用户的编辑 / 权限组 / 重置密码 / 删除按钮会被禁用。操作自己的资料不受限制，但自己的**状态**不可修改。
>
> 系统初始用户（uid=0）的编辑、权限组、删除按钮**已禁用**（服务端也会拒绝）。

### 4.4 权限组管理（`/user-groups`）

**列表**：ID、级别、名称、描述、类型标记（受保护 / 成员锁定 / 普通 / 基本用户权限）、成员数、权限数

**操作**：

| 操作 | 所需权限 | 说明 |
|------|---------|------|
| 新建 / 编辑 | `user-group:create` / `user-group:update` | 名称、描述、**级别**与「基本用户权限」 |
| 成员 | `user-group:manage-members` | 按用户 ID 添加、列表中移除；受保护组不允许增删 |
| 权限 | `user-group:manage-permissions` | 按分类勾选，保存后**整体替换**该组权限 |
| 删除 | `user-group:delete` | 受保护组、以及级别不低于自己的组按钮禁用 |

**级别（Level）**：数值越小权限越高 —— 初始用户 0、超级管理员 2、管理员 10、普通用户 20，新建默认 100。

**基本用户权限**：布尔标记，默认关闭。勾选后该组成为**新注册用户的默认权限组**——注册成功时账号会被自动加入所有带此标记且未锁定成员的组（当前仅默认勾选「普通用户」组）；取消勾选不会移除已加入的成员。

> **只能设置低于自身的级别**：级别高于或等于自己的权限组，其编辑/删除按钮会被禁用；新建时也不能指定高于或等于自身的级别（服务端会返回 `617`）。初始用户组（0）的级别不可修改。
>
> **特殊权限限制**：`PERM_A`（最高权限）只能分配给"初始用户"组；`PERM_B`/`PERM_C` 受相应限制。

### 4.5 权限管理（`/permissions`）

**列表**：按**归属**分区 ——「UMS」（内置、无归属）、各**子系统**（按子系统自注册的权限码各自成组，组名为子系统名称）、「自定义权限」（业务创建、无归属）；分区内一律**按 ID 升序**；列含 ID、代码、名称、分类、类型（特殊 / 内置 / 自定义）、描述

**筛选**：来源（全部 / UMS / 子系统 / 自定义）、分类、关键词（权限代码或名称）

> **子系统的权限来自哪里**：子系统登记后，由子系统自身凭凭证调用 `PUT /api/subsystems/self/permissions` **自注册**（权限码必须以 `{子系统标识}:` 开头）；UCS 会创建并归属该子系统，随后归入对应分组展示。管理端不再代为声明权限码；删除子系统时其权限一并级联删除。

**操作**：

| 操作 | 所需权限 | 说明 |
|------|---------|------|
| 新建 | `permission:create` | 填写权限代码（建议 `模块:操作` 格式）、名称、分类、描述 |
| 编辑 | `permission:update` | 仅可改名称与描述（特殊权限与系统内置权限禁用） |
| 删除 | `permission:delete` | 特殊权限与系统内置权限禁用；二次确认 |

### 4.6 系统设置（`/settings`）

| 区块 | 内容 |
|------|------|
| 站点信息 | 站点名称、LOGO 地址、允许登录开关、允许注册开关 |
| 友情链接 | 增删行并编辑名称 / 链接 / 描述 / LOGO，保存时自动过滤空行 |

> 侧栏顶部的标题取自这里的**站点名称**；未设置时显示默认名称。

> 查看需 `system-setting:read`，保存需 `system-setting:update`。仅具备读权限时按只读模式展示。

### 4.7 邮件服务（`/settings/smtp`）

在界面维护 SMTP 配置，**不再依赖部署配置文件**，保存即时生效。

| 字段 | 说明 |
|------|------|
| 启用邮件服务 | 停用后系统不再发送任何邮件（注册验证等会返回明确业务错误） |
| SMTP 服务器地址 / 端口 | 必填；端口 465 = SSL 直连，587 = STARTTLS，其他端口按下方开关 |
| 账号 / 密码 | 密码**留空表示不修改**；已设置时显示"已保存密码" |
| 发件人地址 / 显示名 | 发件人地址必填 |
| 非标准端口使用 SSL | 勾选为 SSL 直连，取消为 STARTTLS |

**连通性测试**：填写收件人邮箱后点击「发送测试邮件」，直接使用**已保存的配置**发信。

> 查看需 `smtp:read`，保存与测试需 `smtp:update`。
>
> **安全**：密码经 ASP.NET Core Data Protection 加密存储，接口返回值一律脱敏（仅返回 `hasPassword`）。

### 4.8 子系统管理（`/subsystems`）

登记接入 UCS 的外部系统并签发访问凭证。

| 操作 | 所需权限 | 说明 |
|------|---------|------|
| 登记子系统 | `subsystem:create` | 填写名称、标识、描述、主页、有效期；创建后**一次性展示** ApiKey 与 ApiSecret |
| 编辑 | `subsystem:update` | 修改名称、描述、主页、有效期（不影响凭证） |
| 启用 / 停用 | `subsystem:update` | 停用后凭证立即失效 |
| 重置密钥 | `subsystem:update` | 重新生成 ApiKey 与 Secret，**旧凭证立即失效**，新 Secret 仅展示一次 |
| 删除 | `subsystem:delete` | 二次确认，删除后凭证立即失效 |

**登记表单字段**：

| 字段 | 说明 |
|------|------|
| 永不过期 | 勾选后凭证长期有效，有效期控件不可用 |
| 有效期 | 日期选择器（精确到日），有效期至所选日期当日结束 |

**凭证使用方式**（详见接入指南 3.1.2 / 6.6）：

```bash
curl -X POST http://localhost:5000/api/permissions/check \
  -H "X-Api-Key: ucs_xxxxxxxxxxxxxxxx" \
  -H "X-Api-Secret: xxxxxxxxxxxxxxxxxxxxxxxx" \
  -H "Content-Type: application/json" \
  -d '{ "userId": 10001, "codes": ["user:read", "report:export"] }'
```

> ⚠️ **ApiSecret 只在创建或重置时展示一次**，请立即妥善保存；UCS 仅存 SHA256 哈希，无法再次查看。
>
> 子系统凭证的有效范围被刻意收敛为"需要认证但不要求用户权限"的接口（即权限检查接口），无法调用其他业务接口。

### 4.9 无权限提示（`/forbidden`）

权限不足时的三层反馈：

1. **菜单不显示**：无权限的菜单项不会出现在侧边导航中（预防）
2. **页面内提示**：直接输入 URL 访问无权限页面时，页面顶部显示「权限不足」告警，且列表不含任何数据（API 已返回 403）
3. **专用页面**：被重定向到 `/forbidden` 时，页面会提示所需权限与联系管理员建议

### 4.10 我的权限（`/my-permissions`）

从**右上角用户菜单**进入，只读展示当前账号的有效权限：

| 项目 | 说明 |
|------|------|
| 用户 ID | 当前登录用户的 uid |
| 权限级别 | 所属权限组中最高的级别（数值越小权限越高） |
| 有效权限码 | 服务端展开后的结果（含 A/B/C 特殊权限的覆盖效果），来源于所属权限组 |

> 该页面只回答"我有什么权限"，权限的创建与分配在「权限管理」「权限组管理」中完成。

### 4.11 运行日志（`/logs`）

实时查看服务端应用日志（内存环形缓冲，重启即清空）：

| 项目 | 说明 |
|------|------|
| 拉取方式 | 增量拉取 `GET /api/admin/logs?afterId=N&limit=500`，默认每 3 秒自动刷新 |
| 级别过滤 | 全部 / Debug / Information / Warning / Error / Fatal（切换后从头重新拉取） |
| 自动滚动 | 可开关；开启时新日志到达后自动滚动到底部 |
| 容量 | 前端最多保留 2000 条，超出丢弃最旧记录 |

> 需要 `admin:server` 权限（`PERM_C` 覆盖）。

---

## 5. 菜单与权限对应表

| 菜单 | 路由 | 可见所需权限 |
|------|------|-------------|
| 仪表盘 | `/` | 登录即可（统计卡片需 `admin:stats`） |
| 服务器状态 | `/server` | `admin:server` |
| 运行日志 | `/logs` | `admin:server` |
| 用户管理 | `/users` | `user:list` |
| 权限组 | `/user-groups` | `user-group:read` |
| 权限 | `/permissions` | `permission:read` |
| 系统设置 | `/settings` | `system-setting:read` |
| 邮件服务 | `/settings/smtp` | `smtp:read` |
| 子系统 | `/subsystems` | `subsystem:read` |

> 菜单与页面内按钮依据 `GET /api/users/me/permissions` 返回的**展开后有效权限**显隐（`PERM_A` 展开为全部权限，`PERM_B`/`PERM_C` 并入各自覆盖集合）。
>
> 前端显隐**只是体验优化**，真正的鉴权由 API 端点的权限过滤器执行——即使通过其他方式发起请求，也会被服务端拒绝。

### 安全模型说明（重要）

管理前端的登录态保存在 **SignalR circuit** 中（JWT 存于服务端 circuit 内存 + 受保护的浏览器 sessionStorage），**HTTP 页面请求本身不携带凭证**。因此：

- 页面端点在 HTTP 层是**匿名**的（`MapRazorComponents(...).AllowAnonymous()`）。这是必要的：.NET 8 会把页面上的 `[Authorize]` 作为端点元数据交给 `AuthorizationMiddleware` 在 HTTP 层执行，而 HTTP 请求没有凭证，必然 401，导致浏览器直接访问页面失败。
- **登录判定改由 circuit 内的 `AuthorizeRouteView` 执行**（它读取页面的 `[Authorize]` 特性，不依赖端点元数据），未登录会自动跳转到登录页。
- **真正的数据与权限边界始终是 REST API**：所有数据都通过服务端回环 HTTP 调用 API 获取，无有效 JWT 一律 401，权限不足一律 403。
- 页面外壳（HTML 骨架）不含任何业务数据，且未登录时在 circuit 中即被重定向，因此放开端点级校验**不降低安全性**。

---

## 6. 常见问题

**Q：点击按钮没有任何反应？**

先看页面是否弹出了「交互通道连接失败」提示。管理前端的交互完全依赖 `/_blazor` 长连接（WebSocket，失败时回退 SSE / 长轮询）；若反向代理未转发 `Upgrade`/`Connection` 头、或开启了响应缓冲，连接会建立失败——此时页面能正常显示，但**所有按钮都无响应**。

排查顺序：

1. 浏览器开发者工具 → Network → 过滤 `_blazor`，确认连接状态（101 或 200）
2. 服务端日志搜索 `[UI] circuit established`：**有记录**说明通道正常，问题在后续 API 调用；**无记录**即可确定为代理通道问题
3. 检查代理配置（见第 7 节「部署要点」）

**Q：页面提示"无法连接接口服务"？**

控制台通过服务端回环 HTTP 调用本进程 API。基地址默认按**浏览器实际访问的 scheme + host** 解析，一般无需配置；仅当显式设置了 `AppSettings:SelfBaseUrl` 时才需确认该地址在本机可访问（其 Host 会自动加入 `AllowedHosts` 白名单）。

**Q：刷新页面要重新登录？**

正常情况不会。若频繁发生，请确认浏览器未禁用 `sessionStorage`；另外**新开标签页需要重新登录**（会话按标签页隔离，这是有意的安全设计）。

**Q：邮件服务页面提示"密码解密失败"或邮件发不出去？**

SMTP 密码加密依赖 Data Protection 密钥环：默认目录为 `keys/`（可用 `AppSettings:DataProtectionKeysPath` 指定）。**若容器重建导致密钥环丢失，已存密码无法解密**，表现为邮件服务降级为"未配置"。此时在页面重新录入密码即可恢复。

**Q：修改了权限组权限，为什么用户还是旧权限？**

权限缓存默认 5 分钟，但通过管理控制台修改权限组权限、删除用户等操作会**主动清除对应用户缓存**，通常即时生效。若仍异常，可让用户重新登录。

**Q：能在控制台创建新用户吗？**

不能。UCS 的用户创建入口是**注册接口**（`POST /api/users/register`），控制台负责的是用户的管理与维护（编辑、分组、重置密码、删除）。如需批量导入，请通过 API 完成。

**Q：控制台支持移动端吗？**

布局在窄屏下会收起侧边栏并压缩内边距，可基本使用，但定位是**桌面端管理后台**，建议在 1280px 及以上宽度使用。

---

## 7. 部署要点

### 7.1 反向代理（必须）

管理前端的交互依赖 `/_blazor` 长连接，代理必须允许 WebSocket 升级**或**至少关闭响应缓冲以支持 SSE 回退：

```nginx
location / {
    proxy_pass         http://127.0.0.1:5000;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;      # WebSocket 必需
    proxy_set_header   Connection "upgrade";       # WebSocket 必需
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_buffering    off;                        # SSE 回退必需
    proxy_read_timeout 3600s;                      # 长连接不被中断
}
```

Caddy 默认支持 WebSocket 与流式响应，无需额外配置。

### 7.2 代理头与真实客户端地址

应用已在管道最前启用 `UseForwardedHeaders`（`X-Forwarded-For/Proto/Host`），使 `Request.Scheme`/`Host` 反映真实客户端。

> 默认**仅信任回环代理**（框架默认 `KnownNetworks`/`KnownProxies`）。若代理位于其他主机或容器，需先登记其地址再启用，否则转发头会被忽略。

### 7.3 回环自调用与 AllowedHosts

管理控制台以服务端 HTTP 回环方式调用本进程 API，因此**存在两个来源的 Host**：浏览器访问的域名、以及回环调用地址。生产环境 `AllowedHosts` 通常只登记对外域名，为避免回环地址被 `HostFiltering` 以 400 拒绝：

- 基地址默认取**浏览器实际访问的 scheme + host**（必然已通过校验），无需额外配置
- 启动时还会自动把推导出的本机地址（如 `localhost:5000`）加入 `AllowedHosts` 白名单
- 因此即使 `AllowedHosts` 只配置了对外域名，控制台也能正常工作

### 7.4 容器化注意事项

- 数据库连接串通过系统变量 `UCS_DB_CONNECTION` 提供，**不要写入镜像或配置文件**
- `keys/`（Data Protection 密钥环）须挂载持久卷，否则 SMTP 密码在容器重建后无法解密
- 确认 `ASPNETCORE_URLS` 与实际暴露端口一致（仅影响启动日志与降级地址）

---

## 8. 视觉与样式规范

界面采用**工业化冷淡风格**，全部样式集中在 `wwwroot/app.css` 顶部 CSS 变量：

| 维度 | 规范 |
|------|------|
| 底色 / 文字 | 纯白 `#FFFFFF` 底、墨黑 `#111111` 文字 |
| 层级色 | 灰阶 `#4D4D4D` / `#8A8A8A`，浅底 `#FAFAFA` / `#F2F2F2` |
| 分隔线 | 1px 细线 `#E2E2E2`，强调用 `#111111` |
| 圆角 / 阴影 | 全部为 **0**，无阴影、无渐变 |
| 语义色 | 仅成功 `#1F6F43`、危险 `#B3261E`、警示 `#7A5B00`；**仅用于文字与描边，不作填充** |
| 字体 | `PingFang SC`；代码/ID/数值使用等宽字体 |
| 尺寸 | 侧栏 216px（收起 52px）、顶栏 52px |

**图标**：统一使用 **Lucide** 开源图标包（ISC License），不自行绘制图形。

- 组件：`Components/Shared/UiIcon.razor`，用法 `<UiIcon Name="users" Size="16" />`
- 数据：`Components/Shared/UiIconData.cs`（逐字取自 Lucide 官方 24×24 图标文件）
- 描边默认 1.6，颜色继承 `currentColor`

> Ant Design 组件样式在 `app.css` 末尾统一覆盖为直角 / 黑白 / 无阴影（该组件库的样式表未使用 CSS 变量，故按类覆盖）。

---

## 9. 二次开发指引

控制台源码位于以下目录：

```
Components/                    # Blazor 组件
├── App.razor                  # 根组件（HTML 骨架、资源注册、连接诊断提示）
├── Routes.razor               # 路由表（AuthorizeRouteView）
├── Layout/                    # MainLayout / NavMenu / TopBar / EmptyLayout
├── Shared/
│   ├── PageContainer.razor    # 统一页头 / 操作区 / 内容容器
│   ├── PageHeaderService.cs   # 页头标题的 circuit 级状态
│   ├── PermissionGate.razor   # 按有效权限控制显隐
│   ├── RedirectToLogin.razor  # 未登录跳转
│   ├── UcsLineChart.razor     # 轻量 SVG 折线图组件
│   ├── UcsChartModels.cs      # 折线图数据模型
│   ├── UcsNotice.razor        # 内联提示条
│   ├── UiIcon.razor           # Lucide 图标组件
│   └── UiIconData.cs          # Lucide 官方图标路径数据
└── Pages/                     # 各功能页面（每个页面对应一个 .razor 文件）

Auth/                          # 认证与授权状态
├── AuthSchemes.cs                       # 认证方案名（Bearer / SubsystemKey / MultiAuth）
├── TokenStore.cs                        # circuit 级会话状态
├── SessionPersistence.cs                # 会话的浏览器侧受保护持久化
├── LoginSession.cs                      # 登录/登出时的会话装配与清理
├── JwtAuthenticationStateProvider.cs    # 认证状态提供者（含刷新恢复）
├── SubsystemKeyAuthenticationHandler.cs # 子系统 ApiKey 认证处理器
└── PermissionState.cs                   # 有效权限集合缓存

Logging/                       # 日志基础设施
├── SerilogConfig.cs                     # Serilog 启动配置
├── MemoryLogSink.cs                     # 内存环形缓冲（供「运行日志」页读取）
├── LoggingCircuitHandler.cs             # circuit 建立/关闭记录（排障用）
└── StartupFailureReporter.cs            # 启动失败时的可读错误输出

Extensions/                    # 服务注册与端点扩展
├── ServiceCollectionExtensions.cs       # DI 注册
├── WebApplicationExtensions.cs          # 管线装配
├── PermissionEndpointExtensions.cs      # 端点权限过滤器扩展
├── PrivilegeLevelExtensions.cs          # 权限级别判定扩展
└── UserExtensions.cs                    # 用户信息映射扩展

Services/Api/                  # 前端 API 访问层
├── UcsApiClient.cs            # 统一拆包 / 注入令牌 / 自动刷新 / 401 处理 / 传输异常兜底
├── SelfBaseUrlProvider.cs     # 回环调用基地址解析
├── ApiResult.cs               # 结果与分页结果封装
└── EndpointDataModels.cs      # 服务端匿名对象响应的模型

wwwroot/app.css               # 全局样式与设计令牌
```

**新增页面步骤**：

1. 在 `Components/Pages/` 新建 `Xxx.razor`，添加 `@page "/xxx"` 与 `@attribute [Authorize]`
2. 用 `<PageContainer Title="...">` 包裹内容（注意：含 `<Actions>` 时需用 `<ChildContent>` 显式包裹主体）
3. 数据访问通过 `@inject UcsApiClient Api`，调用 `Api.GetAsync<T>` / `Api.GetPagedAsync<T>` / `Api.PostAsync<T>` 等
4. 权限控制用 `<PermissionGate Code="@PermissionCodes.XxxRead">` 包裹需授权的元素
5. 图标用 `<UiIcon Name="..." />`，图标名取 Lucide 官方名称（新增图标需同时补充 `UiIconData.Paths`）
6. 在 `Components/Layout/NavMenu.razor` 的 `MenuGroups` 中登记菜单项（含所需权限码）

**文案约定**：页头只保留标题，不添加说明性副标题；仅保留字段级必要约束（如密码留空语义、Secret 仅显示一次）。设计令牌见第 8 节。
