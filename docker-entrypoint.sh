#!/bin/sh
set -eu

PORT="${PORT:-10000}"
# Bind on all interfaces. Render injects PORT (default 10000).
# The ASP.NET 8 runtime image sets ASPNETCORE_HTTP_PORTS=8080; drop it so
# Kestrel follows ASPNETCORE_URLS instead of the image default.
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"
unset ASPNETCORE_HTTP_PORTS

exec dotnet DocumentsApp.Api.dll
