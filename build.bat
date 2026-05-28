@echo off
chcp 65001 > nul
echo ============================================
echo   ProgressBarTimer - Build Script
echo ============================================
echo.

:: Option 1: .NET SDK
where dotnet >nul 2>&1
if %ERRORLEVEL% == 0 (
    echo [Option 1] .NET SDK found. Building self-contained EXE...
    echo.
    dotnet publish timer.csproj ^
        -c Release ^
        -r win-x64 ^
        --self-contained true ^
        -p:PublishSingleFile=true ^
        -p:EnableCompressionInSingleFile=true ^
        -p:DebugType=none ^
        -o publish\
    if %ERRORLEVEL% == 0 (
        copy /Y publish\timer.exe timer.exe > nul
        echo.
        echo ================================================
        echo  Build succeeded! Run timer.exe to start.
        echo  (timer.exe has been copied to current folder)
        echo ================================================
        goto :done
    ) else (
        echo.
        echo [Option 1] Failed. Trying Option 2...
        echo.
    )
)

:: Option 2: .NET Framework csc.exe
echo [Option 2] Looking for .NET Framework csc.exe...

set "CSC="
for %%v in (v4.0.30319) do (
    if exist "%WINDIR%\Microsoft.NET\Framework64\%%v\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework64\%%v\csc.exe"
        goto :found_csc
    )
    if exist "%WINDIR%\Microsoft.NET\Framework\%%v\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework\%%v\csc.exe"
        goto :found_csc
    )
)

:found_csc
if not defined CSC (
    echo.
    echo [ERROR] No compiler found.
    echo   Please install .NET SDK from:
    echo   https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo Found: %CSC%
echo.
"%CSC%" ^
    /target:winexe ^
    /out:timer.exe ^
    /win32icon:assets\app-icon.ico ^
    /reference:System.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Core.dll ^
    /resource:assets\app-icon.ico,ProgressBarTimer.assets.app-icon.ico ^
    /resource:assets\buttons\btn_minus.png,ProgressBarTimer.assets.buttons.btn_minus.png ^
    /resource:assets\buttons\btn_play.png,ProgressBarTimer.assets.buttons.btn_play.png ^
    /resource:assets\buttons\btn_pause.png,ProgressBarTimer.assets.buttons.btn_pause.png ^
    /resource:assets\buttons\btn_plus.png,ProgressBarTimer.assets.buttons.btn_plus.png ^
    timer.cs

if %ERRORLEVEL% == 0 (
    echo.
    echo ================================================
    echo  Build succeeded! Run timer.exe to start.
    echo  Note: Requires .NET Framework 4.x
    echo  (pre-installed on Windows 10/11)
    echo ================================================
    goto :done
) else (
    echo.
    echo [ERROR] Compilation failed.
    echo   Please install .NET SDK from:
    echo   https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

:done
echo.
pause
