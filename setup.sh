#!/bin/bash
# ============================================================
#  文档管理系统 · 一键启动脚本
#  用法：bash ~/projects/docs-app/setup.sh
# ============================================================

set -e

export PATH="$HOME/.dotnet:$HOME/.npm-global/bin:/opt/homebrew/bin:$PATH"
export HTTPS_PROXY=http://127.0.0.1:7892
export HTTP_PROXY=http://127.0.0.1:7892

echo "════════════════════════════════════════════════════"
echo "  文档管理系统 · 环境准备 + 开发启动"
echo "════════════════════════════════════════════════════"
echo ""

# ---------- ① 环境检查 ----------
echo "【1/4】环境检查"
echo "  .NET: $(dotnet --version 2>/dev/null || echo '未找到')"
echo "  Claude Code: $(which claude 2>/dev/null || echo '未找到')"
echo "  代理端口 7892: $(nc -z 127.0.0.1 7892 && echo '开着 ✅' || echo '没开 ⚠️')"
echo ""

# ---------- ② 安装 LibreOffice（可选，装不上就跳过）----------
echo "【2/4】LibreOffice（Office 文件在线预览用）"
if [ -d "/Applications/LibreOffice.app" ]; then
    echo "  已安装，跳过 ✅"
else
    echo "  开始下载（约 400MB，走代理）..."
    echo "  如果太慢可以 Ctrl+C 中断，不影响主功能（只是 Office 预览不可用）"
    echo ""
    if brew install --cask libreoffice; then
        echo "  ✅ LibreOffice 安装成功"
    else
        echo "  ⚠️  LibreOffice 安装失败/跳过 —— 后续只支持 PDF/图片/文本预览"
    fi
fi
echo ""

# ---------- ③ Claude Code 实现 ----------
echo "【3/4】启动 Claude Code 按规格实现（预计 10-30 分钟）"
echo "  规格文件: ~/projects/docs-app/SPEC.md"
echo ""
cd ~/projects/docs-app

claude -p "$(cat <<'PROMPT'
请阅读当前目录下的 SPEC.md，严格按规格实现这个文档管理系统。

要求：
1. 在 ~/projects/docs-app/ 下创建 DocumentsApp/ 项目（结构见 SPEC.md 第三节）
2. 实现所有数据模型、服务、Controller、前端页面（完整可运行）
3. 关键点：
   - .NET 8，SQLite + EF Core（Code First，启动时自动建库 + 创建初始管理员）
   - Cookie 认证（账号密码登录，不在代码里写死密码）
   - 文件夹多级树 CRUD
   - 文档上传/下载/删除/重命名
   - 预览：PDF 直接返回；Office 用 LibreOffice 转 PDF（路径 /Applications/LibreOffice.app/Contents/MacOS/soffice）；图片/文本直接返回
   - 前端放 wwwroot/，纯 HTML/CSS/JS（无构建）
   - 手机自适应（<768px 侧栏变抽屉）
   - 权限预留：IPermissionService 接口 + AdminPermissionService（全放行）+ FolderPermissions 表
3. 所有面向用户的文本用中文，代码注释用中文
4. 完成后运行 dotnet build 确保编译通过，把错误修完
5. 最后报告：项目路径、编译结果、启动命令

严格按 SPEC.md 的技术选型和结构，不要自行更改。不要问我问题，直接实现。
PROMPT
)" \
  --allowedTools "Write,Edit,Read,Bash,Glob,Grep,WebFetch" \
  --permission-mode acceptEdits

echo ""
echo "【4/4】编译验证"
cd ~/projects/docs-app/DocumentsApp 2>/dev/null || cd ~/projects/docs-app
if dotnet build 2>&1 | tail -10; then
    echo ""
    echo "════════════════════════════════════════════════════"
    echo "  ✅ 完成！"
    echo ""
    echo "  启动服务："
    echo "    cd ~/projects/docs-app/DocumentsApp/src/DocumentsApp.Api"
    echo "    dotnet run --urls 'http://0.0.0.0:8900'"
    echo ""
    echo "  手机访问："
    echo "    bash ~/bin/serve-web.sh 8900"
    echo "════════════════════════════════════════════════════"
fi
