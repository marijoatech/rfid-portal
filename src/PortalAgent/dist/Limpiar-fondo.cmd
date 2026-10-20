@echo off
cd /d "%~dp0"
echo.
echo Se va a borrar el fondo aprendido del lector configurado.
if not exist "PortalAgent.exe" (
  echo No se encontro PortalAgent.exe en esta carpeta.
  pause
  exit /b 1
)
type nul > "limpiar-fondo.flag"
timeout /t 3 /nobreak >nul
echo.
echo Solicitud enviada. Revisa logs\portal-agent.log.
pause
