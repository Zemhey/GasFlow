#!/usr/bin/env bash
# dotnet 包装器：补齐 Bash 环境缺失的 Windows 路径变量
# 缺失 ProgramFiles(x86)/ProgramData/APPDATA 会导致 NuGet 报
# "Value cannot be null. (Parameter 'path1')"
exec env \
  'ProgramFiles(x86)=C:\Program Files (x86)' \
  'ProgramFiles=C:\Program Files' \
  'ProgramData=C:\ProgramData' \
  'APPDATA=C:\Users\Administrator\AppData\Roaming' \
  'LOCALAPPDATA=C:\Users\Administrator\AppData\Local' \
  'SystemRoot=C:\Windows' \
  'windir=C:\Windows' \
  'DOTNET_CLI_TELEMETRY_OPTOUT=1' \
  'DOTNET_NOLOGO=1' \
  dotnet "$@"
