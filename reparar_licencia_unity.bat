@echo off
:: ============================================================
:: Reparar "Activation of your license failed" - Unity / Hub
:: Ejecutar COMO ADMINISTRADOR (clic derecho > Ejecutar como admin)
:: ============================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Ejecuta este script como Administrador.
    pause & exit /b 1
)

echo [1/5] Cerrando Unity, Unity Hub y Licensing Client...
taskkill /f /im "Unity.exe" >nul 2>&1
taskkill /f /im "Unity Hub.exe" >nul 2>&1
taskkill /f /im "Unity.Licensing.Client.exe" >nul 2>&1

echo [2/5] Sincronizando el reloj del sistema (causa #1 del error)...
w32tm /resync >nul 2>&1
if %errorlevel% neq 0 (
    net start w32time >nul 2>&1
    w32tm /resync >nul 2>&1
)

echo [3/5] Borrando licencia corrupta/caducada...
if exist "%PROGRAMDATA%\Unity\Unity_lic.ulf" del /f /q "%PROGRAMDATA%\Unity\Unity_lic.ulf"
if exist "%LOCALAPPDATA%\Unity\licenses" rmdir /s /q "%LOCALAPPDATA%\Unity\licenses"

echo [4/5] Borrando tokens de sesion del Hub...
if exist "%APPDATA%\UnityHub\secureStorage.json" del /f /q "%APPDATA%\UnityHub\secureStorage.json"
if exist "%APPDATA%\UnityHub\Cache" rmdir /s /q "%APPDATA%\UnityHub\Cache"

echo [5/5] Abriendo Unity Hub para reactivar...
start "" "%PROGRAMFILES%\Unity Hub\Unity Hub.exe"

echo.
echo LISTO. Ahora en Unity Hub:
echo   1. Inicia sesion con tu cuenta Unity
echo   2. Ajustes (engranaje) ^> Licenses ^> Add ^> Get a free personal license
echo   3. Abre el proyecto normalmente
echo.
pause
