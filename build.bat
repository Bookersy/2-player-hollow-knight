@echo off
rem Change this if Hollow Knight is installed somewhere else:
set HK=C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight\hollow_knight_Data\Managed

cd /d "%~dp0"
dotnet build -c Release -p:HollowKnightRefs="%HK%"
echo.
pause
