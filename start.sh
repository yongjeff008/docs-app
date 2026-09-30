#!/bin/bash
# ============================================================
#  文档管理系统 · 启动脚本
#  用法：bash ~/projects/docs-app/start.sh
# ============================================================

export PATH="$HOME/.dotnet:$PATH"
cd ~/projects/docs-app/DocumentsApp/src/DocumentsApp.Api || exit 1

echo "════════════════════════════════════════════"
echo "  启动文档管理系统"
echo "════════════════════════════════════════════"
echo ""

# 检查是否已在运行
if lsof -nP -iTCP:8900 -sTCP:LISTEN >/dev/null 2>&1; then
    echo "⚠️  端口 8900 已被占用（可能已在运行）"
    echo "   如需重启，先执行：pkill -f 'dotnet run'"
    echo ""
else
    echo "① 编译并启动服务（端口 8900）..."
    nohup dotnet run --urls "http://0.0.0.0:8900" > /tmp/docsapp.log 2>&1 &
    echo "   等待启动..."
    for i in $(seq 1 30); do
        if curl -s --max-time 2 http://localhost:8900 >/dev/null 2>&1; then
            echo "   ✅ 服务已启动"
            break
        fi
        sleep 1
    done
fi

echo ""
echo "② 本地验证..."
HTTP=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:8900/login.html 2>/dev/null)
echo "   /login.html → HTTP $HTTP"

echo ""
echo "③ 暴露到公网（Tailscale Funnel）..."
/Applications/Tailscale.app/Contents/MacOS/Tailscale funnel --bg 8900 2>&1 | head -5

echo ""
echo "════════════════════════════════════════════"
echo "  ✅ 完成！"
echo ""
# 公网地址与局域网 IP 运行时获取，不写死在脚本里
TS_DNS=$(/Applications/Tailscale.app/Contents/MacOS/Tailscale status --json 2>/dev/null \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))' 2>/dev/null)
LAN_IP=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)
echo "  📱 手机访问（任何网络）："
echo "     https://${TS_DNS:-<Tailscale 域名>}/"
echo ""
echo "  💻 电脑访问：http://${LAN_IP:-localhost}:8900"
echo ""
echo "  📋 查看日志：tail -f /tmp/docsapp.log"
echo "  🛑 停止服务：pkill -f 'dotnet run'"
echo "════════════════════════════════════════════"
