@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title WPE 启动器
cd /d "%~dp0"

rem ---- locate dotnet ----
set "DOTNET="
where dotnet >nul 2>nul && set "DOTNET=dotnet"
if not defined DOTNET if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not defined DOTNET (
  echo [错误] 未找到 dotnet。请安装 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  pause & exit /b 1
)

rem ---- list game packages under games\ ----
set /a N=0
for /d %%D in ("games\*") do (
  if exist "%%D\game.json" (
    set /a N+=1
    set "GAME_!N!=%%D"
  )
)
if %N%==0 (
  echo [错误] 在 games\ 下没有找到游戏包（每个游戏需含 game.json）。
  pause & exit /b 1
)

echo ==================================================
echo    WPE 启动器 - 选择游戏包
echo ==================================================
for /l %%i in (1,1,%N%) do (
  call echo   %%i. %%GAME_%%i%%
)
set /p "SEL=输入编号 [1-%N%] 直接回车=1: "
if not defined SEL set "SEL=1"
set "GAMEDIR=!GAME_%SEL%!"
if not defined GAMEDIR (
  echo [错误] 无效的编号 %SEL%
  pause & exit /b 1
)

echo.
echo [1/2] 构建引擎...
"%DOTNET%" build Wpe.sln -v q -nologo
if errorlevel 1 (
  echo [错误] 构建失败。
  pause & exit /b 1
)

set "APP=src\Wpe.App\bin\Debug\net8.0-windows\Wpe.App.exe"
if not exist "%APP%" (
  echo [错误] 找不到 %APP%
  pause & exit /b 1
)

echo [2/2] 启动 %GAMEDIR% ...
start "" "%APP%" "%CD%\%GAMEDIR%"
exit /b 0
