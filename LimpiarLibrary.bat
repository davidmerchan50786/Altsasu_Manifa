@echo off
echo Limpiando Library de Unity...
echo ASEGURATE de que Unity esta CERRADO antes de continuar.
echo.
pause

set LIB=E:\Desk\DAM\Altsasu_Manifa\Library
set TEMP_PROJ=E:\Desk\DAM\Altsasu_Manifa\Temp

if exist "%LIB%" (
    rmdir /s /q "%LIB%"
    echo OK: Library eliminada.
) else (
    echo Library ya no existe.
)

if exist "%TEMP_PROJ%" (
    rmdir /s /q "%TEMP_PROJ%"
    echo OK: Temp eliminado.
)

echo.
echo Listo. Abre Unity - tardara 2-5 minutos en regenerar la Library.
pause
