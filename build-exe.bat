@echo off
rem Tao file AdminDocChecker.exe chay doc lap (khong can cai .NET tren may chay).
rem Can cai .NET 8 SDK tren may build: https://dotnet.microsoft.com/download/dotnet/8.0

echo Dang tao file exe, vui long doi...
dotnet publish AdminDocChecker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

if errorlevel 1 (
    echo.
    echo Co loi khi build. Hay chup man hinh thong bao loi o tren de duoc ho tro.
    pause
    exit /b 1
)

echo.
echo Xong! File chay nam o: publish\AdminDocChecker.exe
echo Luu y: giu file rules.json cung thu muc voi file exe.
pause
