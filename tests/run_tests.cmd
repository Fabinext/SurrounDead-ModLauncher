@echo off
rem Builds the launcher and runs the core self tests (no network, temp folders only)
call "%~dp0..\build.cmd" || exit /b 1
"%~dp0SelfTest.exe"
