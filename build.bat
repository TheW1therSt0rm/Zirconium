@echo off
dotnet clean
if %errorlevel% neq 0 pause & exit /b %errorlevel%
dotnet build
if %errorlevel% neq 0 pause & exit /b %errorlevel%
dotnet run
if %errorlevel% neq 0 pause & exit /b %errorlevel%
pause
