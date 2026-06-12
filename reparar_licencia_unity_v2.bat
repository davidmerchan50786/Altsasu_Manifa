@echo off
:: ============================================================
:: V2 - Fix "ERROR.LICENSE.FAILED_TO_REFRESH (reading 'some')"
:: Reset profundo del estado de Unity Hub + Licensing Client
:: Ejecutar COMO ADMINISTRADOR
:: ============================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Clic derecho ^> Ejecutar como administrador.
    pause & exit /b 1
)

echo [1/6] Cerrando procesos...
taskkill /f /im "Unity.exe" >nul 2>&1
taskkill /f /im "Unity Hub.exe" >nul 2>&1
taskkill /f /im "Unity.Licensing.Client.exe" >nul 2>&1
taskkill /f /im "UnityLicensingClient_V1.exe" >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/6] Copia de seguridad del estado del Hub (por si acaso)...
if exist "%APPDATA%\UnityHub" (
    if exist "%APPDATA%\UnityHub.bak" rmdir /s /q "%APPDATA%\UnityHub.bak"
    move "%APPDATA%\UnityHub" "%APPDATA%\UnityHub.bak" >nul
)

echo [3/6] Restaurando solo la lista de proyectos y editores...
mkdir "%APPDATA%\UnityHub" >nul 2>&1
if exist "%APPDATA%\UnityHub.bak\projects-v1.json" copy "%APPDATA%\UnityHub.bak\projects-v1.json" "%APPDATA%\UnityHub\" >nul
if exist "%APPDATA%\UnityHub.bak\editors-v2.json" copy "%APPDATA%\UnityHub.bak\editors-v2.json" "%APPDATA%\UnityHub\" >nul
if exist "%APPDATA%\UnityHub.bak\editors.json" copy "%APPDATA%\UnityHub.bak\editors.json" "%APPDATA%\UnityHub\" >nul

echo [4/6] Limpiando TODO el estado de licencias del sistema...
if exist "%PROGRAMDATA%\Unity\Unity_lic.ulf" del /f /q "%PROGRAMDATA%\Unity\Unity_lic.ulf"
if exist "%PROGRAMDATA%\Unity\config" rmdir /s /q "%PROGRAMDATA%\Unity\config"
if exist "%LOCALAPPDATA%\Unity\licenses" rmdir /s /q "%LOCALAPPDATA%\Unity\licenses"
if exist "%LOCALAPPDATA%\Unity\config" rmdir /s /q "%LOCALAPPDATA%\Unity\config"
if exist "%LOCALAPPDATA%\Unity\Unity.Licensing.Client" rmdir /s /q "%LOCALAPPDATA%\Unity\Unity.Licensing.Client"

echo [5/6] Resincronizando reloj...
net start w32time >nul 2>&1
w32tm /resync >nul 2>&1

echo [6/6] Abriendo Unity Hub limpio...
start "" "%PROGRAMFILES%\Unity Hub\Unity Hub.exe"

echo.
echo HECHO. En el Hub: inicia sesion ^> Settings ^> Licenses ^> Add
echo   ^> "Get a free personal license".
echo.
echo Si el error 'reading some' REAPARECE incluso tras esto:
echo   1. Desinstala Unity Hub (solo el Hub, NO borra tus editores ni proyectos)
echo   2. Descarga e instala el Hub nuevo: https://unity.com/download
echo   3. Tus proyectos se re-anyaden con "Add project from disk"
echo.
echo (Tu estado anterior del Hub quedo en %%APPDATA%%\UnityHub.bak por si lo necesitas)
pause
