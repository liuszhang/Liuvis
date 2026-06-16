@echo off
:: Kill any process using port 5127
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":5127" ^| findstr "LISTENING"') do (
    echo Killing process %%a on port 5127...
    taskkill //F //PID %%a 2>nul
)
echo Starting Liuvis...
dotnet run --project src/Liuvis.Web/Liuvis.Web.csproj
