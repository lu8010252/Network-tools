@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 没找到 dotnet,请先安装 .NET 8 SDK:
    echo        https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo ============================================
echo  网络工具箱 一键编译
echo   1. 小体积版  (几 MB,电脑需装 .NET 8 桌面运行时)
echo   2. 独立版    (约 70~150 MB,不用装任何运行时,拷到哪都能跑)
echo ============================================
set /p choice=请选择 [1/2,默认 1]: 
if "%choice%"=="2" goto selfcontained

:framework
dotnet publish NetworkTools.csproj -c Release -r win-x64 --self-contained false -o dist
goto done

:selfcontained
dotnet publish NetworkTools.csproj -c Release -r win-x64 --self-contained true -p:EnableCompressionInSingleFile=true -o dist

:done
if errorlevel 1 (
    echo.
    echo [失败] 编译出错,请看上面的报错信息。
    pause
    exit /b 1
)
echo.
echo [完成] 生成的程序: %~dp0dist\NetworkTools.exe
pause
