@echo off
rem Pulse & Mind Study を見本データ入りで起動する（ダブルクリックで実行）
chcp 65001 >nul
cd /d "%~dp0"
title Pulse ^& Mind Study

where dotnet >nul 2>nul
if errorlevel 1 goto nosdk
dotnet --list-sdks | findstr /b "10." >nul
if errorlevel 1 goto nosdk

rem データベース: LocalDB があれば使い、無ければ SQLite（このフォルダの pulsemind-demo.db）を使う
where sqllocaldb >nul 2>nul
if errorlevel 1 (
    set "Database__Provider=Sqlite"
    set "ConnectionStrings__DefaultConnection=Data Source=pulsemind-demo.db"
)

rem 見本のアカウント（開発用。本番では作られない）
set "Demo__Email=demo@example.com"
set "Demo__Password=Demo!2026pass"

echo.
echo   Pulse ^& Mind Study を起動しています。初回は 1〜2 分かかります。
echo   準備ができると、ブラウザが自動で開きます。
echo.
echo   ログイン: demo@example.com / Demo!2026pass
echo   終了するときは、この画面で Ctrl + C を押すか、画面を閉じてください。
echo.

rem 起動が終わったらブラウザを開く
start "" /b powershell -NoProfile -WindowStyle Hidden -Command "for ($i = 0; $i -lt 300; $i++) { try { if ((Invoke-WebRequest 'http://localhost:5074/healthz' -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { Start-Process 'http://localhost:5074/Account/Login'; break } } catch { } ; Start-Sleep -Seconds 1 }"

dotnet run --project src\PulseMind.Web --launch-profile http
echo.
echo   アプリが終了しました。
pause
goto :eof

:nosdk
echo.
echo   .NET 10 SDK が見つかりません。
echo   開いたページから「.NET 10 SDK」の Windows x64 版をインストールしてから、もう一度このファイルをダブルクリックしてください。
echo.
start "" https://dotnet.microsoft.com/download/dotnet/10.0
pause
