# UCS 业务系统接入指南

> 版本：v1.5.0 | 最后更新：2026-09-15（权限组新增「基本用户权限」`isDefaultForNewUsers`，注册时自动加入；`GET /api/users/me/permissions` 新增返回 `level`；补全内置权限代码与端点清单）

---

## 目录

1. [系统概述](#1-系统概述)
2. [统一响应格式](#2-统一响应格式)
3. [认证与鉴权](#3-认证与鉴权)
4. [权限模型](#4-权限模型)
5. [API 接口参考](#5-api-接口参考)
6. [业务系统集成方案](#6-业务系统集成方案)
7. [错误码速查](#7-错误码速查)
8. [速率限制](#8-速率限制)
9. [附录：SDK 封装建议](#9-附录sdk-封装建议)

---

## 1. 系统概述

UCS（User Management System）是一个统一的用户管理与权限中心，为多个业务系统提供：

- **用户账户管理** — 注册、登录、资料维护、邮箱验证、密码修改
- **JWT 认证** — 签发 Access Token + Refresh Token，无状态水平扩展
- **RBAC 权限模型** — 用户 → 权限组 → 权限 的三层关联，支持 ABC 三级特殊权限
- **批量权限检查** — 一次请求检查多条权限，适合子系统页面/接口级鉴权
- **关注系统** — 用户关注/粉丝、可见性控制
- **系统设置** — 站点信息、友情链接、运行时配置

### 架构简图

```
┌───────────┐   ┌───────────┐   ┌───────────┐
│   BWB     │   │   IHS     │   │  其他系统   │
└─────┬─────┘   └─────┬─────┘   └─────┬─────┘
      │               │               │
      └───────────────┼───────────────┘
                      │  HTTP + JSON
                      ▼
              ┌───────────────┐
              │   UCS 认证中心  │
              │  (JWT + RBAC) │
              └───────────────┘
```

---

## 2. 统一响应格式

所有接口均返回以下 JSON 结构：

```json
{
  "code": 0,
  "message": "操作成功",
  "data": { ... },
  "timestamp": "2026-07-24T12:00:00Z"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `code` | int | 业务状态码，`0` 表示成功，非 `0` 表示失败 |
| `message` | string | 人类可读的描述信息 |
| `data` | object/null | 响应数据，可能为 `null` |
| `timestamp` | string | UTC 时间戳 |

**分页响应**（列表类接口）：

```json
{
  "code": 0,
  "message": "获取成功",
  "page": 1,
  "pageSize": 10,
  "totalCount": 100,
  "totalPages": 10,
  "timestamp": "2026-07-24T12:00:00Z",
  "items": [ ... ]
}
```

---

## 3. 认证与鉴权

### 3.1 认证方式

UCS 支持两种**并存**的认证身份。

#### 3.1.1 用户身份（JWT Bearer Token）

UCS 使用 **JWT Bearer Token** 进行身份认证。所有需认证的接口必须在请求头中携带：

```
Authorization: Bearer <access_token>
```

#### 3.1.2 子系统身份（API Key 凭证）⭐

子系统若需要**服务端直接调用**（没有用户登录态），可使用 UCS 管理后台签发的访问凭证：

```
X-Api-Key: ucs_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
X-Api-Secret: <凭证 Secret，仅在签发时展示一次>
```

凭证由 UCS 管理员在管理控制台「子系统」页面登记签发，也可通过 `POST /api/subsystems` 创建获取。

| 特性 | 说明 |
|------|------|
| 凭证构成 | `X-Api-Key`（公开标识，可随时查看）+ `X-Api-Secret`（密钥，仅存 SHA256 哈希，明文只展示一次） |
| 有效范围 | **仅可访问"需要认证但不要求用户权限"的接口**，即 `POST /api/permissions/check`，且必须显式传入 `userId` |
| 失效方式 | 管理台停用、重新生成密钥、删除子系统后**立即失效**（无需重启 UCS） |
| 有效期 | 登记时可设置过期时间，留空表示长期有效 |
| 使用记录 | UCS 记录每个子系统的 `lastUsedAt`，便于运维排查与清理闲置凭证 |

> **两种身份的关系**：请求携带 `Authorization` 时按用户身份处理，携带 `X-Api-Key` 时按子系统身份处理；两者同时携带时以 `Authorization` 优先。既有的 JWT 调用方行为完全不受影响。
>
> **为什么子系统凭证不能调用其他接口**：需要用户权限的端点依据用户 ID 做权限判定，而子系统身份没有用户上下文，因此会返回 `401`。这是刻意的权限收敛，避免凭证权限过大。

### 3.2 获取 Token

#### 注册

```
POST /api/users/register
Content-Type: application/json

{
  "username": "zhangsan",        // 3-64 字符
  "email": "zhangsan@example.com",
  "password": "MyPass123",       // 8-128 字符
  "extras": {}                   // 可选，自定义扩展字段
}
```

成功响应：

```json
{
  "code": 100,
  "message": "注册成功，请查收验证邮件",
  "data": {
    "email": "zhangsan@example.com",
    "emailSent": true
  }
}
```

> 注册后需验证邮箱才能登录，系统会自动发送验证邮件。

#### 邮箱验证

```
GET /api/users/verify-email?token=<验证链接中的token>
```

#### 登录

```
POST /api/users/login
Content-Type: application/json

{
  "login": "zhangsan",           // 用户名或邮箱
  "password": "MyPass123"
}
```

成功响应：

```json
{
  "code": 200,
  "message": "登录成功",
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "tokenType": "Bearer",
    "expiresIn": 120,             // Access Token 有效期（分钟），取值 = 部署方 Jwt.ExpiryMinutes 配置
    "user": {
      "id": 10001,
      "username": "zhangsan",
      "email": "zhangsan@example.com",
      "phone": null,
      "status": 1,                // 用户状态：1=正常, 2=封禁, 3=未验证, 4=已删除
      "avatarUrl": null,
      "lastLoginIp": "192.168.1.1",
      "lastLoginAt": "2026-07-24T12:00:00Z",
      "createdAt": "2026-01-01T00:00:00Z",
      "emailVerifiedAt": "2026-01-01T00:05:00Z",
      "extras": null
    },
    "refreshToken": "a1b2c3d4e5f6...",
    "refreshTokenExpiresIn": 2592000   // 30 天（秒）
  }
}
```

> **登录失败次数**：同一 IP 5 分钟内失败 20 次、或同一账号 5 分钟内失败 5 次会被临时限流（HTTP 429）；
> 登录成功会立即清零计数。详见 [8. 速率限制](#8-速率限制)。

#### 刷新 Token（避免频繁登录）

```
POST /api/users/refresh
Content-Type: application/json

{
  "refreshToken": "a1b2c3d4e5f6..."
}
```

响应格式与登录相同，返回新的 `token` 和 `refreshToken`。

> **重要**：Refresh Token 轮换后旧 token 立即失效，不可重用。

#### 登出

```
POST /api/users/logout
Authorization: Bearer <access_token>
```

权限：`user:logout`（需认证 + 该权限）

登出会撤销 Refresh Token、将当前 Access Token 加入黑名单，并把用户标记为离线。

### 3.3 Token 生命周期

```text
Access Token          ◄── 短期有效（取值 = 部署方 appsettings.json 的 Jwt.ExpiryMinutes）
Refresh Token         ◄── 长期有效（30 天）
                       │
用户登录 ──► 获得 Access + Refresh
                       │
Access 过期 ──► 用 Refresh 换新 Access + Refresh
                       │
Refresh 过期 ──► 需重新登录
```

**推荐客户端做法**：

1. 存储 `token`、`refreshToken` 和 `expiresIn`
2. 每次请求前检查 Access Token 是否即将过期（如剩余 < 5 分钟）
3. 若即将过期，调用 `/api/users/refresh` 刷新
4. 请求返回 401 时，尝试刷新；刷新失败则跳转登录页

---

## 4. 权限模型

### 4.1 模型结构

```
用户 (User)
  │
  └── 权限组成员 (UserGroupMember) ─── 权限组 (UserGroup)
                                        │
                                        └── 权限组权限 (GroupPermission) ─── 权限 (Permission)
```

- 一个用户可以属于多个权限组
- 一个权限组可以拥有多个权限
- 用户的最终权限 = 所属所有权限组的权限的并集

### 4.2 特殊权限（ABC 三级）

| 权限代码 | 名称 | 覆盖范围 |
|----------|------|----------|
| `PERM_A` | 超级管理员 | **覆盖所有权限**，包括创建/删除权限和权限组 |
| `PERM_B` | 权限管理员 | 覆盖所有 `permission:*` 和 `user-group:*` 权限 |
| `PERM_C` | 服务管理员 | 覆盖 `admin:stats` 和 `admin:server` 权限 |

### 4.3 内置权限代码

| 权限代码 | 说明 |
|----------|------|
| `user:read` | 查看用户信息 |
| `user:create` | 管理员直接创建用户（可跳过邮箱验证） |
| `user:update` | 更新用户信息 |
| `user:delete` | 删除用户 |
| `user:list` | 查看用户列表 |
| `user:change-password` | 修改密码 |
| `user:change-email` | 修改邮箱 |
| `user:logout` | 登出 |
| `user:reset-password` | 重置指定用户密码（管理员操作） |
| `avatar:upload` | 上传头像 |
| `avatar:delete` | 删除头像 |
| `admin:stats` | 查看统计数据 |
| `admin:server` | 查看服务器状态 |
| `permission:read` | 查看权限列表 |
| `permission:create` | 创建权限 |
| `permission:update` | 更新权限 |
| `permission:delete` | 删除权限 |
| `user-group:read` | 查看权限组 |
| `user-group:create` | 创建权限组 |
| `user-group:update` | 更新权限组 |
| `user-group:delete` | 删除权限组 |
| `user-group:manage-members` | 管理权限组成员 |
| `user-group:manage-permissions` | 管理权限组权限 |
| `follow:user` | 关注用户 |
| `follow:read` | 查看关注信息 |
| `follow:manage` | 管理关注可见性 |
| `system-setting:read` | 查看系统设置 |
| `system-setting:update` | 更新系统设置 |
| `smtp:read` | 查看邮件（SMTP）配置 |
| `smtp:update` | 更新邮件（SMTP）配置 |
| `subsystem:read` | 查看子系统 |
| `subsystem:create` | 创建子系统 |
| `subsystem:update` | 更新子系统 |
| `subsystem:delete` | 删除子系统 |

### 4.4 预设权限组

| 权限组 | 拥有权限 | 说明 |
|--------|----------|------|
| 初始用户 | `PERM_A` | 系统内置，成员为 uid=0 的系统账户 |
| 超级管理员 | `PERM_B` | 可管理全部权限和权限组 |
| 管理员 | `PERM_C` | 可查看统计和服务器状态 |
| 普通用户 | 无特殊权限 | 标记为「基本用户权限」，新注册用户自动加入 |

> **基本用户权限（`isDefaultForNewUsers`）**：权限组的布尔标记，默认 `false`。为 `true` 时，**新注册用户**注册成功后会自动加入该组；可同时有多个权限组被标记，注册时会全部加入。已注册用户不受影响，取消标记也不会移除已加入的成员。管理接口见 5.4。

---

## 5. API 接口参考

> 以下 `{id}` 均为用户的 uint64 ID。

### 5.1 用户相关

#### 获取当前用户的有效权限

```
GET /api/users/me/permissions
Authorization: Bearer <token>
```

**权限**：认证即可（无需任何权限码）

**响应**：

```json
{
  "code": 0,
  "message": "获取成功",
  "data": {
    "userId": 10001,
    "level": 20,
    "codes": ["user:read", "user:update", "permission:read", "user-group:read"]
  }
}
```

> `level` 是当前用户的**最高权限级别**（所属权限组中数值最小的 `level`，数值越小权限越高）。不属于任何权限组时为 `2147483647`（`NoGroup`）。
>
> `codes` 是**展开后**的有效权限集合：持有 `PERM_A` 时返回系统内全部权限码；持有 `PERM_B`/`PERM_C` 时会并入其覆盖的权限码。
>
> 该接口用于业务系统前端控制菜单/按钮显隐；**实际鉴权仍应以服务端权限判定或 `POST /api/permissions/check` 为准**，不要仅依赖前端显隐。

#### 获取用户信息

```
GET /api/users/{id}
Authorization: Bearer <token>
```

权限：`user:read`（查看自己不需要）

#### 获取用户列表（分页）

```
GET /api/users?page=1&pageSize=10&status=0&search=keyword
Authorization: Bearer <token>
```

权限：`user:list`

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| page | int | 1 | 页码 |
| pageSize | int | 10 | 每页条数（最大 100） |
| status | int? | null | 筛选：1=正常，2=封禁，3=未验证，4=已删除（对应 `UserStatus` 枚举值） |
| search | string? | null | 搜索用户名或邮箱 |

> 注意：不传 `status` 时返回所有未被软删除的用户（自动排除 status=4）。

#### 更新用户信息

```
PUT /api/users/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "username": "new-name",
  "email": "new@example.com",
  "phone": "13800138000",
  "extras": { "department": "Engineering" },
  "status": 1,
  "isFollowingListHidden": false
}
```

| 字段 | 说明 |
|------|------|
| `username` / `phone` | 唯一性校验（排除已软删除用户） |
| `email` | **仅管理员可为他人修改**；改自己须走 `/api/users/{id}/change-email`，否则返回 `612`。修改后 `emailVerifiedAt` 被清空 |
| `extras` | 任意 JSON，整体覆盖 |
| `status` | `1`=正常、`2`=封禁、`3`=未验证。**仅管理员可为他人修改**（改自己返回 `616`）；不接受 `4`（已删除，请使用删除接口）。管理员可借此**解封**被封禁账号 |
| `isFollowingListHidden` | 是否隐藏自己的关注列表 |
| 头像 | 不在本接口：`POST /api/users/{id}/avatar`（multipart，字段 `file`，≤6MB，需 `avatar:upload`）与 `DELETE /api/users/{id}/avatar`（需 `avatar:delete`） |

> **未传的字段保持不变**（`null` 表示不修改），不是整体替换。
>
> **被封禁账号**：仅允许管理员通过传入 `status` 解封，其余资料修改一律返回 `204 / 403`。

权限：`user:update`（修改自己不需要）

#### 部分更新用户信息

```
PATCH /api/users/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "phone": "13900139000",
  "status": 1
}
```

字段同 `PUT`（不支持 `email`），同样"未传即不改"，`status` 的限制也一致。

权限：`user:update`（修改自己不需要）

#### 修改密码

```
POST /api/users/{id}/change-password
Authorization: Bearer <token>
Content-Type: application/json

{
  "currentPassword": "OldPass123",
  "newPassword": "NewPass456"
}
```

权限：`user:change-password`（仅限本人操作）

> 修改密码后所有 Token 失效，需重新登录。

#### 修改邮箱

```
POST /api/users/{id}/change-email
Authorization: Bearer <token>
Content-Type: application/json

{
  "newEmail": "new@example.com",
  "password": "MyPass123"
}
```

权限：`user:change-email`（仅限本人操作）

> 系统会向新邮箱发送验证邮件，点击链接后生效。确认端点（公开，通过 token 验证）：

```
GET /api/users/verify-email-change?token=<验证链接中的token>
```

> 仅在确认后才真正切换邮箱，且要求该邮箱未被其他用户占用（`code=402`）。

#### 删除用户（软删除）

```
DELETE /api/users/{id}
Authorization: Bearer <token>
```

权限：`user:delete`

#### 获取用户所属权限组

```
GET /api/users/{id}/groups
Authorization: Bearer <token>
```

权限：`user:read`（查看自己不需要）

#### 设置用户所属权限组

```
PUT /api/users/{id}/groups
Authorization: Bearer <token>
Content-Type: application/json

{
  "groupIds": [1, 2, 3]
}
```

权限：`user-group:manage-members`

> **受保护权限组不可分配**：`is_member_locked=true` 的权限组（即"初始用户"组）**不允许新增成员**——请求中包含"该用户尚未加入"的此类组时返回 HTTP 403 与 `1125`，防止通过批量设置越权取得 A 级权限。
>
> 但**允许从该组移除成员**，作为误加成员的修复路径；`uid=0` 始终被保留在初始用户组内，不会被移除。

---

### 5.2 权限相关

#### 获取所有权限

```
GET /api/permissions
Authorization: Bearer <token>
```

权限：`permission:read`

#### 获取自定义权限（无需认证）

```
GET /api/permissions/custom
```

返回非系统内置的自定义权限列表。

#### 创建自定义权限

```
POST /api/permissions
Authorization: Bearer <token>
Content-Type: application/json

{
  "code": "report:export",
  "name": "导出报表",
  "category": "报表",
  "description": "允许用户导出报表数据"
}
```

权限：`permission:create`

#### 更新权限

```
PUT /api/permissions/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "导出报表（新版）",
  "description": "更新后的描述"
}
```

权限：`permission:update`

> 特殊权限（A/B/C）与系统内置权限均不可修改，返回 `code=1013`。

#### 删除权限

```
DELETE /api/permissions/{id}
Authorization: Bearer <token>
```

权限：`permission:delete`

> 特殊权限与系统内置权限均不可删除，返回 `code=1013`。

---

### 5.3 权限检查（核心集成接口）

这是业务子系统接入的**最核心接口**，支持单码和批量两种模式。

> **模式选择规则**：请求中同时提供 `codes` 与 `permissionCode` 时，**批量模式优先**（先判断 `codes`）。
> 当 `codes` 非空时走批量模式；否则才走单码模式（`permissionCode` 为空则返回 `code=2` 参数错误）。
> `userId` 可省略，省略时检查当前登录用户；显式传入**他人** ID 需要 `permission:read` 权限。
>
> **子系统调用方式**：使用 `X-Api-Key` + `X-Api-Secret` 请求头调用时，**必须显式传入 `userId`**（子系统无用户上下文），且不受 `permission:read` 限制（凭证即授权）。认证方式见 [3.1.2](#312-子系统身份api-key-凭证-)。

#### 单码模式（检查单个权限）

```
POST /api/permissions/check
Authorization: Bearer <token>
Content-Type: application/json

{
  "permissionCode": "report:export",
  "userId": 10001        // 可选，不传则检查当前登录用户
}
```

响应：

```json
{
  "code": 1150,
  "message": "检查完成",
  "data": {
    "userId": 10001,
    "permissionCode": "report:export",
    "hasPermission": true
  }
}
```

#### 批量模式（一次检查多个权限）⭐ 推荐子系统使用

```
POST /api/permissions/check
Authorization: Bearer <token>
Content-Type: application/json

{
  "userId": 10001,
  "codes": ["report:view", "report:export", "user:list"]
}
```

响应：

```json
{
  "code": 1150,
  "message": "检查完成",
  "data": {
    "userId": 10001,
    "permissions": [
      { "code": "report:view", "granted": true },
      { "code": "report:export", "granted": false },
      { "code": "user:list", "granted": false }
    ]
  }
}
```

> **批量模式的优势**：一次 HTTP 请求即可完成页面所有按钮/功能的权限判断，避免 N+1 问题。
>
> **权限覆盖逻辑**：系统自动处理 ABC 权限的覆盖。例如用户拥有 `PERM_A`，则所有 `granted` 均为 `true`。

#### 权限检查流程（业务子系统最佳实践）

```text
用户请求业务页面
      │
      ▼
业务系统从请求中提取 JWT Token 中的 userId
      │
      ▼
调用 UCS POST /api/permissions/check (批量模式)
传入 userId + 该页面所需的所有权限码
      │
      ▼
根据返回的 granted 列表控制页面元素的显示/隐藏
```

---

### 5.4 权限组相关

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| GET | `/api/user-groups?page=1&pageSize=20` | `user-group:read` | 分页获取权限组（pageSize 默认 20，上限 100） |
| GET | `/api/user-groups/{id}` | `user-group:read` | 获取权限组详情（含成员和权限） |
| POST | `/api/user-groups` | `user-group:create` | 创建权限组，请求体 `{ "name": "...", "description": "...", "level": 100, "isDefaultForNewUsers": false }` |
| PUT | `/api/user-groups/{id}` | `user-group:update` | 更新权限组；`level` / `isDefaultForNewUsers` 传 `null` 表示不修改 |
| DELETE | `/api/user-groups/{id}` | `user-group:delete` | 删除权限组 |
| POST | `/api/user-groups/{id}/members` | `user-group:manage-members` | 添加成员 `{ "userId": 10001 }` |
| DELETE | `/api/user-groups/{id}/members/{userId}` | `user-group:manage-members` | 移除成员 |
| PUT | `/api/user-groups/{id}/permissions` | `user-group:manage-permissions` | 设置组权限 `{ "permissionIds": [1,2,3] }` |

> **受保护组限制**：初始权限组（`isMemberLocked=true, isProtected=true`）不可编辑/删除/增删成员；
> 超级管理员组（`isProtected=true`）不可删除、不可修改权限，但可调整成员。
> 受保护组操作失败返回 `code=1113`。
>
> **权限组级别**：权限组含 `level` 字段（数值越小权限越高：初始用户 0 / 超级管理员 2 / 管理员 10 / 普通用户 20 / 新建默认 100）。
> 创建 / 更新 / 删除权限组时，目标组级别必须**严格大于**操作者自身级别（数值更大），否则返回 `403 + 617`。
> 创建时可在请求体中传 `level`，更新时 `level` 留空表示不修改。
>
> **基本用户权限**：`isDefaultForNewUsers`（默认 `false`）标记该组为「新注册用户的默认权限组」，注册成功时会自动加入所有被标记且未锁定成员的组。

---

### 5.5 系统与管理

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| GET | `/api/system/info` | 无 | 获取系统基本信息 |
| GET | `/api/system/friend-links` | 无 | 获取友情链接 |
| GET | `/api/avatars/{filename}` | 无 | 读取头像文件 |
| GET | `/api/admin/stats/online-users` | `admin:stats` | 在线用户数 |
| GET | `/api/admin/stats/total-users` | `admin:stats` | 总用户数 |
| GET | `/api/admin/stats/new-users?days=7` | `admin:stats` | 近 N 天新注册数 |
| GET | `/api/admin/stats/active-users?days=7` | `admin:stats` | 近 N 天活跃数 |
| GET | `/api/admin/stats/new-users/daily?days=7` | `admin:stats` | 每日趋势（新增 / 活跃 / 注销），供仪表盘折线图使用 |
| GET | `/api/admin/server/status` | `admin:server` | 服务器状态（CPU、内存、网络、数据库） |
| GET | `/api/admin/server/metrics?hours=24` | `admin:server` | 历史指标采样（支持 6 / 24 / 72 / 168 小时） |
| GET | `/api/admin/logs?afterId=0&limit=500&level=` | `admin:server` | 增量获取运行日志（内存环形缓冲，`level` 可选） |
| POST | `/api/admin/email/test` | `admin:server` | 发送测试邮件 |
| GET | `/api/admin/system/settings` | `system-setting:read` | 获取所有系统设置 |
| GET | `/api/admin/system/settings/{key}` | `system-setting:read` | 获取单个设置项 |
| PUT | `/api/admin/system/settings/{key}` | `system-setting:update` | 更新单个设置项 |
| PUT | `/api/admin/system/info` | `system-setting:update` | 更新系统信息 |
| PUT | `/api/admin/system/friend-links` | `system-setting:update` | 更新友情链接 |
| GET | `/api/users/me/permissions` | 认证即可 | 获取当前用户展开后的有效权限码 |
| POST | `/api/admin/users` | `user:create` | 管理员创建用户（不发送验证邮件；密码留空则生成并一次性返回） |
| POST | `/api/admin/users/{id}/reset-password` | `user:reset-password` | 重置指定用户密码（不传 `newPassword` 则生成随机密码并一次性返回） |
| GET | `/api/admin/smtp/config` | `smtp:read` | 获取邮件服务配置（密码脱敏） |
| PUT | `/api/admin/smtp/config` | `smtp:update` | 保存邮件服务配置（密码留空表示不修改） |
| POST | `/api/admin/smtp/test` | `smtp:update` | 使用当前配置发送测试邮件 |
| GET | `/api/subsystems?page=1&pageSize=20&search=` | `subsystem:read` | 子系统列表（分页 + 关键字搜索） |
| GET | `/api/subsystems/{id}` | `subsystem:read` | 子系统详情 |
| POST | `/api/subsystems` | `subsystem:create` | 登记子系统并签发凭证（Secret 仅展示一次） |
| PUT | `/api/subsystems/{id}` | `subsystem:update` | 更新子系统元数据与有效期（不影响凭证） |
| PUT | `/api/subsystems/{id}/status` | `subsystem:update` | 启用/停用子系统 `{ "isEnabled": true }` |
| POST | `/api/subsystems/{id}/regenerate-secret` | `subsystem:update` | 重新生成凭证（旧凭证立即失效） |
| DELETE | `/api/subsystems/{id}` | `subsystem:delete` | 删除子系统并使其凭证立即失效 |
| PUT | `/api/subsystems/self/permissions` | 子系统凭证 | 子系统以自身凭证自注册其声明的权限码 |

> **权限归属（`permissions.subsystem_id`）**：子系统权限**不再由管理员代声明**，而是由子系统凭自身凭证调用 `PUT /api/subsystems/self/permissions` 自注册（`PUT` 为**全量同步**语义）：
> - 权限码必须以 `{子系统 code}:` 开头，长度 ≤64，字符仅允许 `[a-z0-9:_-]`；
> - 权限码归属他方或与 UMS 内置/自定义权限冲突时，**整个请求被拒绝**（`code=1201`，响应带冲突清单，不产生半提交）；
> - 清单外、原归属本子系统的权限会被**删除**，同时级联清除权限组授权并失效受影响用户的权限缓存；
> - 删除子系统（`DELETE /api/subsystems/{id}`）同样会级联删除其全部权限。
>
> `GET /api/permissions` 会为每条权限返回 `subsystemId` 与 `subsystemName`，便于区分权限来源。

---

### 5.6 关注系统

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| POST | `/api/users/{userId}/follow` | `follow:user` | 关注用户 |
| DELETE | `/api/users/{userId}/follow` | `follow:user` | 取消关注 |
| GET | `/api/users/{userId}/follow/status` | `follow:read` | 检查是否已关注 |
| GET | `/api/users/me/following?page=1&pageSize=10` | `follow:read` | 我的关注列表 |
| GET | `/api/users/me/followers?page=1&pageSize=10` | `follow:read` | 我的粉丝列表 |
| GET | `/api/users/{userId}/following` | `follow:read` | 指定用户的关注列表 |
| GET | `/api/users/{userId}/followers` | `follow:read` | 指定用户的粉丝列表 |
| GET | `/api/users/{userId}/follow/stats` | `follow:read` | 关注统计 |
| GET | `/api/users/me/follow/stats` | `follow:read` | 我的关注统计 |
| PUT | `/api/users/me/following/visibility` | `follow:manage` | 设置关注列表可见性 `{ "isHidden": true }` |
| GET | `/api/users/me/following/visibility` | `follow:manage` | 获取可见性设置 |

---

## 6. 业务系统集成方案

### 6.1 推荐架构

```
┌──────────────────────────────────────────────────────┐
│                    业务子系统 (BWB/IHS/...)             │
│                                                      │
│  ┌─────────────┐   ┌─────────────┐   ┌────────────┐ │
│  │ AuthFilter   │   │ Permission  │   │ UCS Client │ │
│  │ (JWT验证)    │   │ Guard       │   │ (API封装)  │ │
│  └──────┬───────┘   └──────┬──────┘   └─────┬──────┘ │
│         │                  │                │        │
└─────────┼──────────────────┼────────────────┼────────┘
          │                  │                │
          ▼                  ▼                ▼
    验证 Token 签名     调用权限检查 API    调用用户数据 API
    (本地密钥验证)      POST /api/permissions/check   GET /api/users/{id}
```

### 6.2 方案 A：独立认证（推荐）

业务系统**不**维护自己的用户表，完全依赖 UCS：

1. **前端**用户通过 UCS 的登录页（或你们自己的 UI 调用 UCS 登录 API）获得 JWT Token
2. **后端**收到请求后，使用与 UCS 相同的对称密钥（HMAC-SHA256）本地验证 JWT 签名（无需网络调用）
3. **后端**需要判断权限时，调用 `POST /api/permissions/check`（批量模式）

**优点**：用户数据不冗余，权限实时生效，架构清洁。

> ⚠️ **Issuer/Audience 必须与 UCS 部署的 `appsettings.json` 中 `Jwt:Issuer`、`Jwt:Audience` 完全一致**，
> 否则本地验签会失败。以下示例中的值对应 UCS 默认配置。

**JWT 验证（本地密钥方式）**：

```csharp
    // JWT 使用对称密钥（HMAC-SHA256），需与 UCS 共享 Secret
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "UserManagementAPI",       // 与 UCS Jwt:Issuer 一致
            ValidateAudience = true,
            ValidAudience = "UserManagementClient",  // 与 UCS Jwt:Audience 一致
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("<与UCS相同的JWT Secret>")),
            ClockSkew = TimeSpan.Zero   // 与 UCS 一致（零偏移，严格按 exp 过期）
        };
    });
```

**Node.js 示例**：

```javascript
const jwt = require('jsonwebtoken');

function verifyToken(token) {
  return jwt.verify(token, '<UCS_JWT_SECRET>', {
    issuer: 'UserManagementAPI',      // 与 UCS Jwt:Issuer 一致
    audience: 'UserManagementClient', // 与 UCS Jwt:Audience 一致
    clockTolerance: 0                 // 与 UCS 一致（零时钟偏移）
  });
}

// 从 token 中提取 userId
function getUserIdFromToken(token) {
  const decoded = jwt.decode(token);
  // UCS JWT 中 userId 存储在 nameid 声明中（详见下方 claim 表）
  return BigInt(decoded.nameid);
}
```

#### JWT Payload 声明（claims）速查 ⭐

UCS 使用 .NET 默认 claim 映射，**payload 字段名如下**（接入方解析时务必按此取值）：

| Payload 字段 | 含义 | 类型 |
|-------------|------|------|
| `nameid` | **用户 ID（userId）** | 数字字符串，如 `"10001"` |
| `unique_name` | 用户名 | string |
| `email` | 邮箱 | string |
| `jti` | Token 唯一标识（用于黑名单校验） | string |
| `iss` | 签发者 = `UserManagementAPI` | string |
| `aud` | 受众 = `UserManagementClient` | string |
| `exp` / `nbf` / `iat` | 过期/生效/签发时间（Unix 秒） | number |

> ⚠️ **注意**：userId 字段名是 **`nameid`**，**不是** `sub`（UCS 未使用 `sub` 声明）。

### 6.3 方案 B：代理认证

业务系统维护自己的用户表，user_id 与 UCS 保持一致：

1. 前端通过 UCS 登录获得 Token
2. 业务系统后端验证 JWT 签名，从 **`nameid`** 声明提取 userId（见 6.2 claims 速查）
3. 业务系统使用该 userId 查询自己的数据库关联数据
4. 权限判断仍调用 UCS

**适用场景**：需要存储用户附加的业务数据（如订单、积分等）。

### 6.4 权限集成代码示例

#### C# 示例

```csharp
public class UcsPermissionClient
{
    private readonly HttpClient _http;

    public UcsPermissionClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// 批量检查用户权限（推荐）
    /// </summary>
    public async Task<Dictionary<string, bool>> CheckPermissionsAsync(
        ulong userId, string[] codes, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/permissions/check")
        {
            Content = JsonContent.Create(new { userId, codes })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<BulkCheckResult>>();

        return result!.Data.Permissions.ToDictionary(p => p.Code, p => p.Granted);
    }
}

// 使用示例 - 在页面控制器中
var permissions = await _ucsClient.CheckPermissionsAsync(
    userId,
    new[] { "report:view", "report:create", "report:delete" },
    accessToken
);

bool canView = permissions.GetValueOrDefault("report:view", false);
bool canCreate = permissions.GetValueOrDefault("report:create", false);
bool canDelete = permissions.GetValueOrDefault("report:delete", false);
```

#### JavaScript/TypeScript 示例

```typescript
interface PermissionCheckItem {
  code: string;
  granted: boolean;
}

interface BulkCheckResponse {
  userId: number;
  permissions: PermissionCheckItem[];
}

async function checkPermissions(
  userId: number,
  codes: string[],
  accessToken: string
): Promise<Map<string, boolean>> {
  const response = await fetch('http://ucs-host/api/permissions/check', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${accessToken}`
    },
    body: JSON.stringify({ userId, codes })
  });

  const { data } = await response.json();
  const map = new Map<string, boolean>();
  for (const item of (data as BulkCheckResponse).permissions) {
    map.set(item.code, item.granted);
  }
  return map;
}

// 使用
const perms = await checkPermissions(
  userId,
  ['dashboard:view', 'dashboard:edit'],
  accessToken
);
if (perms.get('dashboard:edit')) {
  showEditButton();
}
```

### 6.5 服务发现与配置

```json
// appsettings.json
{
  "Ucs": {
    "BaseUrl": "https://ucs.your-domain.com",
    "JwtSecret": "<与UCS配置相同的Secret>",
    "Timeout": 10
  }
}
```

建议使用 `IHttpClientFactory` 注册 UCS 客户端：

```csharp
builder.Services.AddHttpClient<IUcsClient, UcsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ucs:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

---

### 6.6 方案 C：子系统凭证（服务端直连）

当子系统在**服务端**调用 UCS（无用户登录态）——例如定时同步、后台批量校验权限——使用 3.1.2 介绍的子系统凭证：

```bash
# 1. 由 UCS 管理员在管理控制台「子系统」页面登记，并保存一次性展示的 ApiSecret
# 2. 调用权限检查接口（必须显式传入 userId）
curl -X POST https://api.example.com/api/permissions/check \
  -H "X-Api-Key: ucs_xxxxxxxxxxxxxxxx" \
  -H "X-Api-Secret: xxxxxxxxxxxxxxxxxxxxxxxx" \
  -H "Content-Type: application/json" \
  -d '{ "userId": 10001, "codes": ["user:read", "report:export"] }'
```

**C# 示例**：

```csharp
public sealed class UcsSubsystemClient
{
    private readonly HttpClient _http;

    public UcsSubsystemClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["Ucs:BaseUrl"]!);
        _http.DefaultRequestHeaders.Add("X-Api-Key", config["Ucs:ApiKey"]);
        _http.DefaultRequestHeaders.Add("X-Api-Secret", config["Ucs:ApiSecret"]);
    }

    /// <summary>批量检查指定用户的权限，返回 code → granted 映射</summary>
    public async Task<Dictionary<string, bool>> CheckAsync(ulong userId, string[] codes, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/permissions/check", new { userId, codes }, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<BulkResult>>(cancellationToken: ct);
        return envelope!.Data!.Permissions.ToDictionary(p => p.Code, p => p.Granted);
    }

    private sealed record Envelope<T>(int Code, string? Message, T? Data);
    private sealed record BulkResult(ulong UserId, List<Item> Permissions);
    private sealed record Item(string Code, bool Granted);
}
```

**Node.js 示例**：

```javascript
async function checkPermissions(userId, codes) {
  const res = await fetch(`${process.env.UCS_BASE_URL}/api/permissions/check`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': process.env.UCS_API_KEY,
      'X-Api-Secret': process.env.UCS_API_SECRET
    },
    body: JSON.stringify({ userId, codes })
  });

  const body = await res.json();
  if (!res.ok) throw new Error(body.message);

  return Object.fromEntries(body.data.permissions.map(p => [p.code, p.granted]));
}
```

> **凭证安全**：通过配置中心或环境变量注入，不要硬编码到代码库。建议**为每个子系统单独签发凭证**，便于审计与单独吊销。
>
> **注意**：子系统凭证只能调用权限检查与权限自注册接口；其余业务接口仍需用户 JWT。

### 6.7 权限自注册（推荐）

子系统凭凭证**自行声明**自己需要的权限码，无需管理员代为录入。流程：

1. 管理员在「子系统」页面登记子系统，签发并保存一次性展示的 ApiSecret；
2. 子系统凭凭证调用自注册端点，**全量同步**自己的权限码；
3. 子系统被删除时，其注册的权限（含用户组授权关联）**一并级联删除**。

**接口**：`PUT /api/subsystems/self/permissions`

```bash
curl -X PUT https://api.example.com/api/subsystems/self/permissions \
  -H "X-Api-Key: ucs_xxxxxxxxxxxxxxxx" \
  -H "X-Api-Secret: xxxxxxxxxxxxxxxxxxxxxxxx" \
  -H "Content-Type: application/json" \
  -d '{
    "codes": [
      { "code": "bbs:post:create", "name": "发布帖子" },
      { "code": "bbs:post:delete", "name": "删除帖子" }
    ]
  }'
```

**成功响应**（HTTP 200）：

```json
{
  "code": 1200,
  "message": "同步成功",
  "data": { "added": 2, "updated": 0, "deleted": 0, "conflicts": [] }
}
```

**规则**：

| 规则 | 说明 |
|------|------|
| 前缀强制 | 权限码必须以「`{子系统标识}:`」开头（如子系统标识为 `bbs`，则所有码须形如 `bbs:post:create`），长度 ≤64，仅允许 `[a-z0-9:_-]` |
| 全量同步 | PUT 语义：清单外、此前由本子系统注册的权限会被**直接删除**（建议每次启动时以完整清单调用） |
| 冲突拒绝 | 目标码已归属其他子系统、或为 UCS 内置/自定义权限时，返回 HTTP 409（code=1201），本次同步整体不提交 |
| 级联删除 | 子系统被删除时，其注册的权限及用户组授权关联一并删除，受影响用户的权限缓存即时失效 |

> **重复调用**：本接口幂等——每次调用都以请求清单为准做全量对齐（新增缺失、更新名称/描述、删除多余）。
> 建议业务系统在启动时以完整清单调用一次，之后按需增量对齐（凭证经 `X-Api-Key` / `X-Api-Secret` 传递）。

---

## 7. 错误码速查

### 通用码

| code | 含义 |
|------|------|
| 0 | 成功 |
| 1 | 通用错误 |
| 2 | 参数验证错误 |
| 3 | 未授权（Token无效或缺失） |
| 4 | 禁止访问 |
| 5 | 资源不存在 |
| 6 | 服务器内部错误 |
| 7 | 请求过于频繁 |

### 权限相关

| code | 含义 |
|------|------|
| 1000 | 权限不足 |
| 1001 | 权限不存在 |
| 1002 | 权限代码已存在 |
| 1013 | 不能修改/删除特殊权限 |
| 1125 | 受保护权限组（成员锁定）不可分配 |
| 1131 | A 权限不可分配给非初始权限组 |

### 用户相关

| code | 含义 |
|------|------|
| 100 | 注册成功 |
| 101 | 用户名已存在 |
| 102 | 邮箱已被注册 |
| 200 | 登录成功 |
| 201 | 用户名或密码错误 |
| 203 | 账号已注销 |
| 204 | 账号已被封禁 |
| 205 | 登录尝试过于频繁 |
| 207 | 账号未验证邮箱 |
| 616 | 用户状态仅可由管理员修改 / 不支持的状态值 |
| 617 | 目标用户/权限组的权限级别不低于操作者，拒绝操作 |

完整错误码列表见 `ResultCodes.cs`。

---

## 8. 速率限制

| 接口 | 限制策略 |
|------|----------|
| `/api/users/login` | 同 IP 5 分钟 20 次；同账号 5 分钟 5 次 |
| `/api/users/register` | 同邮箱 1 分钟 5 次 |
| `/api/users/resend-verification` | 同邮箱 1 分钟 1 次 |

超限时返回 HTTP 429，`code` 为 `7`。

> 限流计数存储在数据库（`rate_limit_entries` 表），多实例部署时全局限流窗口自动共享。
>
> **计数清零**：仅在**登录失败**时累积计数；**登录成功后**该 IP 与账号的失败计数会被立即清零，
> 因此正常用户的偶发输错密码不会累积到触发锁定。

---

## 9. 附录：SDK 封装建议

### 9.1 最小化接入清单

如果你是业务系统开发者，推荐优先实现以下功能：

1. **[必须]** 调用 `/api/users/login` 和 `/api/users/refresh`，实现登录与 Token 管理
2. **[必须]** 验证 Token：要么本地验签（与 UCS 共享 JWT Secret + Issuer/Audience，见方案 A/6.2），要么每次请求透传给 UCS 校验（见 6.2/6.3 说明）
3. **[推荐]** 调用 `POST /api/permissions/check`（批量模式）进行页面级权限控制
4. **[推荐]** 调用 `GET /api/users/{id}` 获取用户展示信息（头像、用户名等）
5. **[可选]** 调用 `/api/users/{id}/groups` 获取用户所属权限组
6. **[可选]** 若需**服务端直接调用**（无用户登录态），向 UCS 管理员申请**子系统凭证**（`X-Api-Key` + `X-Api-Secret`），用于调用权限检查接口（见 3.1.2 / 6.6）

### 9.2 安全建议

- **Access Token 放在内存中**（如 React state、Vuex/Pinia store），不要存 localStorage
- **Refresh Token 存 httpOnly cookie 或安全存储**，避免 XSS 窃取
- **HTTPS 必须**，生产环境禁止 HTTP 明文传输
- **Token 过期提前刷新**，不要等到 401 再处理（用户体验差）
- **权限检查结果可短期缓存**（如 60 秒），减少 API 调用

### 9.3 常见问题

**Q：如何知道 JWT 中用户的 userId？**

解码 JWT Payload 后取 **`nameid`** 字段（数字字符串，可转为 uint64），例如 `"10001"`。

> 注意不是 `sub` —— UCS 使用 .NET 默认 claim 映射，userId 存储在 `nameid` 中。完整字段清单见 [6.2 节 claims 速查](#jwt-payload-声明claims速查-)。

**Q：权限变更后多久生效？**

权限缓存默认 5 分钟。调用 `DELETE /api/users/{id}` 或设置权限组权限后会主动清除缓存，实时生效。

**Q：可以自定义权限码吗？**

可以。调用 `POST /api/permissions` 创建自定义权限，权限码建议使用 `模块:操作` 格式（如 `report:export`）。

**Q：子系统可以自己管理权限组吗？**

可以。如果管理员拥有 `PERM_B` 权限，可以创建独立的权限组并分配对应的权限。但通常建议由 UCS 管理员统一管理。

**Q：子系统凭证（X-Api-Key）能调用哪些接口？**

只能调用**需要认证但不要求用户权限**的接口，目前即 `POST /api/permissions/check`（必须显式传入 `userId`）。调用需要用户权限的接口会返回 `401` —— 这是刻意的权限收敛设计，避免凭证权限过大。

**Q：子系统凭证泄漏了怎么办？**

让 UCS 管理员在管理控制台「子系统」页面点击「重置密钥」，或调用 `POST /api/subsystems/{id}/regenerate-secret`。**新凭证签发后旧凭证立即失效**，无需重启 UCS，也无需通知其他子系统。

**Q：管理员忘记用户密码怎么办？**

拥有 `user:reset-password` 权限的管理员可调用 `POST /api/admin/users/{id}/reset-password`；请求体不传 `newPassword` 时服务端生成随机密码并**一次性返回**。重置后该用户的 Refresh Token 会被撤销并标记离线，需用新密码重新登录。

**Q：`GET /api/users/me/permissions` 返回的权限码可以直接用于鉴权判断吗？**

可以用于**前端显隐**，但不应作为唯一鉴权依据。它返回的是展开后的权限集合（含 ABC 覆盖效果），与服务端判定逻辑一致；但为避免前端绕过，涉及数据变更的操作仍应由服务端通过 `RequirePermission` 或 `POST /api/permissions/check` 复核。

---

> 📧 如有接入问题，请联系 UCS 系统管理员。
