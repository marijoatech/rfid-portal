# PortalCalibration - consola tecnica de calibracion

Herramienta de puesta en marcha para el lector RFID. **No es la interfaz del
personal**: el uso diario sera en Marijoa web. Se usa en la PC tecnica / notebook
para validar el lector, las antenas, la potencia y el fondo.

## Requisitos

- Windows 10/11 x64 con .NET Framework 4.x.
- Conexion de red al lector: `tcp://192.168.3.60`.
- `PortalCalibration.exe` y `SReaderAPI.dll` en la misma carpeta.

## Uso basico

1. Abrir `PortalCalibration.exe`.
2. Dejar la IP `192.168.3.60` y tocar **Conectar**.
3. Verificar:
   - Modelo `E710 8 canales`.
   - Antenas detectadas: 1-4 hoy; 5-8 cuando esten los cables largos.
   - ROE por antena.
   - Potencia actual por puerto (20 dBm de fabrica).
4. Elegir antenas (**Usar**) y bajar la potencia por puerto segun lo que se vea.
5. Tocar **Aplicar** (detiene la lectura si estaba activa).
6. Tocar **Iniciar** y observar la tabla en vivo: lote, EPC, antena, RSSI y
   cantidad de lecturas.
7. Con la puerta vacia, tocar **Aprender fondo** durante 10 segundos. Se guardan
   los EPC vistos 2 o mas veces; se pueden ver con **Ver fondo**. Si la lectura
   estaba detenida, la consola ofrece iniciarla.
8. Marcar **Ignorar EPC de fondo** para que no ensucien la vista.
9. Guardar el perfil con **Guardar perfil** (`perfil_porton.xml`).

El umbral de RSSI y el fondo se aplican a las lecturas nuevas; **Limpiar** reinicia
la vista. No hay exportacion CSV: todo se mira en pantalla.

## GPO / buzzer

Las pruebas de GPO y buzzer requieren conexion por red (no serial). Detener la
lectura antes de probarlas. El pulso GPO se configura en segundos.

## Problemas frecuentes

- **No conecta**: verificar IP, ping, cable y que la PC pueda rutear a
  `192.168.3.60`. Si la PC esta en `192.168.2.x`, necesita ruteo/firewall hacia
  la red 3.
- **Antena no detectada**: revisar cable coaxil y que el puerto corresponda a la
  barra. Los puertos 5-8 no estan cableados todavia.
- **Cambio de potencia no se nota**: aplicar, detener y volver a iniciar; el
  lector conserva la ultima configuracion.
- **Windows SmartScreen**: el ejecutable no esta firmado; usar "Mas informacion"
  -> "Ejecutar de todas formas".

## Compilar

Ejecutar `build.ps1` en esta carpeta. Genera `dist\PortalCalibration.exe` y
`PortalCalibration.zip`.
