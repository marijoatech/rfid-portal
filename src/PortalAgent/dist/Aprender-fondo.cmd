@echo off
cd /d "%~dp0"
echo.
echo Deje la PUERTA VACIA antes de continuar.
echo El agente aprendera el fondo durante 12 segundos.
echo.
if not exist "PortalAgent.exe" (
  echo No se encontro PortalAgent.exe en esta carpeta.
  pause
  exit /b 1
)
type nul > "entrenar-fondo.flag"
timeout /t 14 /nobreak >nul
echo.
echo Entrenamiento solicitado. Revisa logs\portal-agent.log para ver el resultado.
pause
