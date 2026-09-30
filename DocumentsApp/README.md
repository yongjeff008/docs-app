# 文档管理系统

基于 .NET 8 的文档管理 Web 应用：多级文件夹树、文档上传/下载、PDF / Office / 图片 / 文本在线预览，手机浏览器自适应。

## 技术栈

| 层 | 选型 |
|:---|:-----|
| 后端 | ASP.NET Core 8 Web API（Controller 风格） |
| 数据库 | SQLite + EF Core（Code First Migration，启动时自动迁移） |
| 前端 | 原生 HTML + CSS + JS（`wwwroot/`，无构建步骤） |
| 文件存储 | 本地磁盘 `~/DocumentsApp/storage/`（`files/` 原文件，`previews/` 预览缓存） |
| Office 预览 | LibreOffice headless 转 PDF |
| PDF 渲染 | PDF.js（CDN 引入，不可用时回退到浏览器内置阅读器） |

## 运行

```bash
cd DocumentsApp/src/DocumentsApp.Api
dotnet run --urls "http://0.0.0.0:8900"
```

浏览器打开 <http://localhost:8900>，用初始管理员登录（见下）。
手机与电脑处于同一局域网时，访问 `http://<电脑 IP>:8900` 即可。

首次启动会自动：

- 在当前目录创建 SQLite 数据库 `documents.db` 并应用迁移
- 数据库中没有管理员时，创建初始管理员（密码仅以 PBKDF2 哈希存在数据库里，代码中没有默认密码）：
  - 用户名、密码取自配置 `InitialAdmin:Username` / `InitialAdmin:Password`，
    可用环境变量传入：`InitialAdmin__Username=... InitialAdmin__Password=... dotnet run`
  - 未配置密码时随机生成，写入程序目录下的 `initial-admin.txt`（仅本人可读），登录后请修改密码并删除该文件
- 创建存储目录 `~/DocumentsApp/storage/files/` 与 `previews/`

## 配置（`appsettings.json`）

| 键 | 说明 | 默认值 |
|:---|:-----|:-------|
| `ConnectionStrings:Default` | SQLite 连接串 | `Data Source=documents.db` |
| `Storage:RootPath` | 文件存储根目录 | `/Users/jeff/DocumentsApp/storage` |
| `Storage:MaxFileSizeMB` | 单文件上传上限 | `100` |
| `Preview:LibreOfficePath` | soffice 可执行文件路径 | `/Applications/LibreOffice.app/Contents/MacOS/soffice` |
| `Preview:TimeoutSeconds` | 单次转换超时 | `30` |

未安装 LibreOffice 时，Office 文档预览会提示“服务器未安装 LibreOffice”，其余功能不受影响。

## API 一览

```
POST   /api/auth/login              { username, password }
POST   /api/auth/logout
GET    /api/auth/me

GET    /api/folders                 完整树（嵌套 JSON）
GET    /api/folders/{id}
POST   /api/folders                 { name, parentId? }
PUT    /api/folders/{id}            { name }
PUT    /api/folders/{id}/move       { newParentId }
DELETE /api/folders/{id}            非空返回 409

GET    /api/documents?folderId={id} 文件夹下文档（附加 &q= 可按文件名过滤；仅 q 时全局搜索）
POST   /api/documents/upload        multipart: file, folderId
GET    /api/documents/{id}/download
GET    /api/documents/{id}/preview
PUT    /api/documents/{id}/rename   { fileName }
DELETE /api/documents/{id}
```

```
POST   /api/auth/password           { oldPassword, newPassword }  修改自己的密码
GET    /api/permissions/{folder|document}/{id}         授权详情（需管理权限）
PUT    /api/permissions/{folder|document}/{id}         { inheritPermissions, entries } 继承/断开 + 授权（整体替换）
GET    /api/permissions/{folder|document}/{id}/check?userId=  查询某成员的最终权限及来源
GET    /api/permissions/overview    权限总览（仅管理员）
GET    /api/directory               授权对象列表（部门 + 普通成员）

# 以下仅管理员
GET/POST        /api/users          PUT/DELETE /api/users/{id}   PUT /api/users/{id}/password
GET/POST        /api/departments    PUT/DELETE /api/departments/{id}
GET             /api/audit-logs?page=&pageSize=&q=
```

## 权限管理

- **角色**：管理员（全部权限，可进入右上角“系统管理”）/ 普通成员（按文件夹授权）。
- **授权对象**：单个成员、部门、所有成员。
- **权限级别**（位标志 `PermissionActions`）：

  | 级别 | 可以做什么 |
  |:-----|:----------|
  | 查看者 | 浏览、预览、下载 |
  | 上传者 | + 上传文档、新建子文件夹、重命名文档 |
  | 编辑者 | + 删除文档 |
  | 管理者 | + 重命名 / 移动 / 删除文件夹、设置该文件夹权限 |

- **继承**：授权默认对所有子文件夹及其中的文档生效（可取消“含子文件夹”）；多条授权取并集。
- **断开继承**（文件夹与文档均支持）：断开后上级授权不再生效，只有自身授权生效；断开时可选择复制现有继承权限，
  恢复继承时可选择保留或清除单独授权。顶层文件夹没有上级，无需断开。
- **标志**：橙色断链图标 = 已断开继承；蓝色盾牌图标 = 继承 + 单独授权；无图标 = 完全继承。
  文件夹树、文档列表、顶部“我的权限”标签中均有显示。
- **权限明细**：权限弹窗含“授权设置 / 生效明细 / 查询成员”三页；系统管理 → 权限总览 列出所有断开继承或有单独授权的项目。
- **防误锁**：非管理员保存后若会失去自己的管理权限，保存会被拒绝。
- **所有者**：文件夹创建者自动成为该文件夹的管理者；上传者可删除自己上传的文档。
- **根目录**：只有管理员能在根目录新建 / 移入文件夹。
- **无权访问的上级**：成员只被授权子文件夹时，该子文件夹在树中显示为顶层，不暴露上级名称。
- **会话控制**：禁用、删除账号或修改 / 重置密码后，相关登录会话在下一次请求时立即失效；角色变更无需重新登录即生效。
- **操作日志**：记录登录（含失败）、上传、下载、重命名、删除、文件夹变更、权限设置、用户与部门管理，管理员可在“系统管理 → 操作日志”查询。

授权适用规则集中在 `Services/PermissionGraph.cs`（权限判断与明细展示共用），实现位于 `Services/FolderPermissionService.cs`，在 `Program.cs` 中注册；调试时可换成 `AdminPermissionService`（全部放行）。

## 数据库迁移

迁移文件位于 `src/DocumentsApp.Api/Data/Migrations/`。修改实体后新增迁移：

```bash
cd DocumentsApp
dotnet tool restore
cd src/DocumentsApp.Api
dotnet ef migrations add <迁移名> -o Data/Migrations
```
