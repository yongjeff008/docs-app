# 文档管理系统 · 开发规格 v1.0

> 本文档是交给 Claude Code 实现的完整技术规格。请严格按规格实现，不要自行更改技术选型或架构。

## 一、项目概述

基于 .NET 8 的文档管理系统（Web 应用），支持多级文件夹树、文档上传下载、Office/PDF 在线预览。
当前阶段只需单个管理员账号，但**权限管理需预留完整扩展点**（数据结构 + 服务接口 + API 中间件三层预留）。

## 二、技术栈（不得更改）

| 层 | 选型 |
|:---|:-----|
| 后端 | ASP.NET Core 8 Web API（Controller 风格） |
| 数据库 | SQLite + EF Core（Code First Migration） |
| 前端 | 原生 HTML + CSS + JS（单页应用，**无构建步骤**） |
| 文件存储 | 本地磁盘，根目录 `~/DocumentsApp/storage/` |
| Office 预览 | LibreOffice headless 转 PDF（`soffice --headless --convert-to pdf`） |
| PDF 渲染 | PDF.js（CDN 引入） |

## 三、项目结构

```
DocumentsApp/
├── DocumentsApp.sln
├── src/
│   └── DocumentsApp.Api/
│       ├── DocumentsApp.Api.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   └── DbSeeder.cs
│       ├── Models/
│       │   ├── User.cs
│       │   ├── Folder.cs
│       │   ├── Document.cs
│       │   └── FolderPermission.cs
│       ├── Services/
│       │   ├── IPermissionService.cs      ← 权限预留接口
│       │   ├── AdminPermissionService.cs  ← 当前实现（全放行）
│       │   ├── IFileStorageService.cs
│       │   ├── FileStorageService.cs
│       │   ├── IPreviewService.cs
│       │   └── LibreOfficePreviewService.cs
│       ├── Controllers/
│       │   ├── AuthController.cs
│       │   ├── FoldersController.cs
│       │   └── DocumentsController.cs
│       └── wwwroot/
│           ├── index.html
│           ├── login.html
│           ├── css/app.css
│           └── js/
│               ├── app.js
│               ├── api.js
│               ├── tree.js
│               └── viewer.js
└── README.md
```

## 四、数据模型

### User
```csharp
public class User {
    public int Id { get; set; }
    public string Username { get; set; }      // "admin"
    public string PasswordHash { get; set; }  // PBKDF2 或 BCrypt
    public string DisplayName { get; set; }   // "管理员"
    public string Role { get; set; }          // "Admin"
    public DateTime CreatedAt { get; set; }
}
```

### Folder（多级树）
```csharp
public class Folder {
    public int Id { get; set; }
    public string Name { get; set; }
    public int? ParentId { get; set; }        // null = 根级
    public Folder? Parent { get; set; }
    public List<Folder> Children { get; set; }
    public int SortOrder { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Document
```csharp
public class Document {
    public int Id { get; set; }
    public int FolderId { get; set; }
    public Folder Folder { get; set; }
    public string FileName { get; set; }      // 原始文件名（含扩展名）
    public string StoredPath { get; set; }    // 相对 storage 的路径
    public long Size { get; set; }            // 字节
    public string MimeType { get; set; }
    public string? PreviewPath { get; set; }  // 缓存的预览 PDF 路径
    public DateTime? PreviewGeneratedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### FolderPermission ⭐ 权限预留表
```csharp
[Flags]
public enum PermissionActions {
    None = 0, Read = 1, Write = 2, Delete = 4, Share = 8, Manage = 16
}

public class FolderPermission {
    public int Id { get; set; }
    public int FolderId { get; set; }         // 关联文件夹
    public string SubjectType { get; set; }   // "User" | "Role" | "Department"
    public string SubjectId { get; set; }     // 主体标识
    public PermissionActions Actions { get; set; }
    public bool InheritToChildren { get; set; } = true;  // 向下继承
    public DateTime CreatedAt { get; set; }
}
```

## 五、权限预留的三层设计（重要）

### 第一层：数据层
`FolderPermissions` 表已建好，含 SubjectType/SubjectId/Actions/InheritToChildren。
**当前不写入任何数据**，但表和实体已就位。

### 第二层：服务接口
```csharp
public interface IPermissionService {
    /// 检查用户对某资源是否有某操作权限
    Task<bool> CanAccessAsync(int userId, string resourceType, int resourceId, PermissionActions action);

    /// 获取用户可见的文件夹 ID 列表（用于过滤树）
    Task<List<int>> GetAccessibleFolderIdsAsync(int userId);
}

// 当前实现：管理员全放行
public class AdminPermissionService : IPermissionService {
    public Task<bool> CanAccessAsync(...) => Task.FromResult(true);
    public Task<List<int>> GetAccessibleFolderIdsAsync(int userId) =>
        Task.FromResult(new List<int>());   // 空列表 = 不过滤
}
```

### 第三层：API 中间件
所有受保护的 Controller 加 `[Authorize]` + 在每个写操作前调用 `_permissionService.CanAccessAsync(...)`。
**当前该调用永远返回 true，不产生任何限制**，但调用点已全部埋好。

> 📌 关键：以后要实现真实权限时，**只需替换 `IPermissionService` 的实现类**（改 DI 注册一行），
> 所有 Controller 和前端都无需改动。

## 六、API 设计

### 认证（Cookie Session）
```
POST   /api/auth/login     body: { username, password }  → 200 + Set-Cookie
POST   /api/auth/logout                                    → 204
GET    /api/auth/me                                        → { id, username, displayName, role }
```
- 使用 ASP.NET Core Cookie 认证（`AddAuthentication().AddCookie()`）
- 登录成功后签发票据，有效期 7 天
- 密码用 PBKDF2 哈希存储，首次启动时创建初始管理员（账号密码来自配置或随机生成，不写在代码里）

### 文件夹
```
GET    /api/folders              → 完整树结构（嵌套 JSON）
GET    /api/folders/{id}         → 单个文件夹详情
POST   /api/folders              body: { name, parentId? }  → 创建
PUT    /api/folders/{id}         body: { name }             → 重命名
PUT    /api/folders/{id}/move    body: { newParentId }      → 移动（可选）
DELETE /api/folders/{id}         → 删除（若非空则 409，提示先清空）
```

### 文档
```
GET    /api/documents?folderId={id}     → 该文件夹下文档列表
POST   /api/documents/upload            multipart/form-data: file, folderId
GET    /api/documents/{id}/download     → 下载原始文件
GET    /api/documents/{id}/preview      → 预览（见下方规则）
DELETE /api/documents/{id}
PUT    /api/documents/{id}/rename       body: { fileName }
```

### 预览规则（`GET /api/documents/{id}/preview`）
| 文件类型 | 返回 |
|:---------|:-----|
| PDF | 直接返回原文件（`Content-Type: application/pdf`） |
| Office（doc/docx/xls/xlsx/ppt/pptx） | 用 LibreOffice 转 PDF（缓存到 storage/previews/），返回 PDF |
| 图片（jpg/png/gif/webp） | 直接返回原文件 |
| 文本（txt/md/csv/json） | 返回纯文本（UTF-8） |
| 其他 | 返回 415 + 提示信息 JSON |

**转换命令**：
```bash
soffice --headless --convert-to pdf --outdir <目标目录> <源文件>
```
- 加超时保护（30 秒）
- 转换结果缓存（Document.PreviewPath），源文件更新时清除缓存

## 七、前端设计

### 页面结构

**login.html** — 登录页
- 居中卡片，用户名/密码输入 + 登录按钮
- 简洁设计，渐变背景或纯色

**index.html** — 主界面（单页）
```
┌──────────────────────────────────────────────────────┐
│ 📄 文档管理    [🔍 搜索]              admin ▾         │  顶栏
├──────────────┬───────────────────────────────────────┤
│ 📁 制度文件 ▾ │  采购管理制度.docx        [↓][⋮]      │
│   📁 采购     │  ───────────────────────────────────  │
│   📄 财务     │                                       │
│ 📁 流程文件 ▸ │   ┌─────────────────────────────┐     │
│ 📁 记录表单   │   │                             │     │
│              │   │     预览区（PDF/图片）       │     │
│ ＋新建文件夹  │   │                             │     │
│              │   └─────────────────────────────┘     │
└──────────────┴───────────────────────────────────────┘
   侧栏（树）            主区（列表 / 预览）
```

### 响应式（移动优先）
- **桌面（≥768px）**：侧栏固定显示（280px），主区自适应
- **手机（<768px）**：侧栏收起为抽屉，顶栏左侧有 ☰ 按钮呼出
- 触摸友好：按钮最小 44×44px
- 上传：手机用文件选择器，桌面支持拖拽

### 交互细节
1. **文件夹树**：点击展开/折叠，点击名称选中（高亮），右键/长按出现菜单（重命名、删除、新建子文件夹）
2. **文档列表**：图标（按类型）+ 文件名 + 大小 + 上传时间 + 操作按钮
3. **预览**：点击文档 → 主区切换为预览（含返回列表按钮）；手机全屏预览
4. **上传**：
   - 桌面：拖拽到主区 或 点击"上传"按钮
   - 手机：点击上传 → 选择文件（支持多选）
   - 显示上传进度条
5. **提示**：操作成功/失败用 Toast 提示（中文，页面右上角）

### 样式规范
- 配色：主色 `#3b82f6`（蓝），侧栏 `#f8fafc`，主区 `#ffffff`，文字 `#0f172a`
- 字体：`-apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif`
- 圆角：8px（容器）/ 6px（按钮）
- 阴影：`0 1px 3px rgba(0,0,0,0.08)`
- 图标：用内联 SVG（不用图标库，避免额外依赖）
- 参考风格：Outline（简洁、留白多）、语雀（树形目录）

## 八、配置

### appsettings.json
```json
{
  "ConnectionStrings": {
    "Default": "Data Source=documents.db"
  },
  "Storage": {
    "RootPath": "/Users/jeff/DocumentsApp/storage",
    "MaxFileSizeMB": 100
  },
  "Preview": {
    "LibreOfficePath": "/Applications/LibreOffice.app/Contents/MacOS/soffice",
    "TimeoutSeconds": 30
  }
}
```

### 启动命令（开发）
```bash
cd DocumentsApp/src/DocumentsApp.Api
dotnet run --urls "http://0.0.0.0:8900"
```
> ⚠️ 必须绑 `0.0.0.0`（让局域网/手机可访问）

## 九、验收标准

- [ ] 浏览器打开 `http://localhost:8900` → 跳转登录页
- [ ] 初始管理员登录成功 → 进入主界面
- [ ] 能新建/重命名/删除文件夹，支持多级嵌套
- [ ] 能上传文件到指定文件夹（含进度显示）
- [ ] 能下载、重命名、删除文档
- [ ] 上传 .docx/.xlsx/.pptx → 点击能预览（LibreOffice 转 PDF）
- [ ] 上传 PDF → 直接预览
- [ ] 上传图片 → 直接显示
- [ ] **手机浏览器打开自适应正常**（侧栏抽屉式）
- [ ] 刷新页面保持登录状态

## 十、注意事项

1. **代码注释用中文**
2. **不要引入前端构建工具**（不用 npm/webpack/vite）
3. **不要引入 ORM 之外的复杂依赖**（保持精简）
4. **所有面向用户的文本用中文**
5. **错误处理友好**：文件太大、不支持的类型、转换失败等都要有明确提示
6. **文件存储用 GUID 或哈希命名**，避免文件名冲突和特殊字符问题
7. **删除文件夹时如果非空要拒绝**（提示先删除内容）
