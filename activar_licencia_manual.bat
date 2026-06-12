@echo off
:: ============================================================
:: Activacion MANUAL de licencia Unity (sin sign-in en el Hub)
:: Paso 1: genera el fichero de peticion (.alf) en el Escritorio
:: Paso 2: tu lo subes a la web (login en navegador, que si funciona)
:: Paso 3: vuelve a ejecutar este script con el .ulf descargado
:: ============================================================
setlocal
set EDITOR="%PROGRAMFILES%\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
set LICCLIENT="%PROGRAMFILES%\Unity Hub\UnityLicensingClient_V1\Unity.Licensing.Client.exe"
set ALF="%USERPROFILE%\Desktop\Unity_lic.alf"
set ULF="%USERPROFILE%\Desktop\Unity_v6000.x.ulf"

if exist %ULF% goto :activar

echo [PASO 1] Generando peticion de licencia (.alf)...
if not exist %EDITOR% (
    echo [ERROR] No encuentro Unity 6000.3.10f1 en la ruta estandar.
    echo Edita la variable EDITOR de este script con tu ruta real.
    pause & exit /b 1
)
%EDITOR% -batchmode -createManualActivationFile -logfile "%TEMP%\unity_alf.log" -quit
if exist "Unity_lic.alf" move "Unity_lic.alf" %ALF% >nul
if not exist %ALF% (
    echo [AVISO] El .alf no aparecio donde se esperaba. Busca "Unity_lic.alf"
    echo en esta carpeta o revisa el log: %TEMP%\unity_alf.log
    pause & exit /b 1
)
echo.
echo [PASO 2] Se va a abrir la web de activacion. Haz esto:
echo   1. Inicia sesion AHI (en el navegador si funciona)
echo   2. Sube el fichero del Escritorio: Unity_lic.alf
echo   3. Elige "Unity Personal" ^> descarga el .ulf
echo   4. Guarda el .ulf en el Escritorio
echo   5. Vuelve a ejecutar ESTE script
start "" "https://license.unity3d.com/manual"
pause
exit /b 0

:activar
echo [PASO 3] Activando con el .ulf del Escritorio...
if exist %LICCLIENT% (
    %LICCLIENT% --activate-ulf --license-file %ULF%
) else (
    %EDITOR% -batchmode -manualLicenseFile %ULF% -logfile "%TEMP%\unity_ulf.log" -quit
)
echo.
echo Si no ha dado error: licencia activada. Abre el Hub o el editor directamente.
pause
