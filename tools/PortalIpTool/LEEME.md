# PortalIpTool - leer/cambiar la IP del lector RFID

Herramienta minima para la notebook de puesta en marcha. No requiere instalacion:
es un `.exe` de .NET Framework 4.x (viene con Windows 10/11) + `SReaderAPI.dll`.

## Que hace

- Probar la conexion con el lector por red (`tcp://IP`).
- Leer la configuracion actual (IP, mascara, gateway), modelo/canales, firmware,
  potencia, region y numero de serie.
- Cambiar la IP del lector (IP, mascara y gateway).
- Buscar lectores en la LAN por UDP broadcast (si no se conoce la IP).
- Abrir la configuracion de red de Windows (`ncpa.cpl`).

## Que llevar a la notebook

Copiar la carpeta `dist` completa (contiene `PortalIpTool.exe` y `SReaderAPI.dll`).
No hace falta instalar nada mas. Si Windows SmartScreen avisa por ser un ejecutable
sin firma, usar "Mas informacion" -> "Ejecutar de todas formas".

## Primer contacto (IP de fabrica 192.168.1.136)

1. Conectar el cable de red de la notebook al lector (directo o al mismo switch).
2. En Windows, configurar la placa Ethernet de la notebook:
   - IP: `192.168.1.100`
   - Mascara: `255.255.255.0`
   - Gateway: vacio
   (El boton "Red de Windows" abre `ncpa.cpl`.)
3. Abrir `PortalIpTool.exe`.
4. Con "IP actual" en `192.168.1.136`, tocar **Probar conexion**.
   - Si responde, dice modelo/canales y la IP/mascara/gateway actuales.
   - Si no responde, tocar **Buscar lectores** (misma red; 3 segundos).
5. Completar la nueva configuracion. Para la red 192.168.3.x, por ejemplo:
   - Nueva IP: `192.168.3.136` (confirmar que este libre)
   - Mascara: `255.255.255.0`
   - Gateway: `192.168.3.1` (confirmar el gateway real de la red)
6. Tocar **Cambiar IP del lector** y confirmar.
7. **Apagar y volver a encender el lector** (el cambio se aplica al reiniciar).
8. Cambiar la notebook a la red nueva:
   - IP: `192.168.3.100`
   - Mascara: `255.255.255.0`
   - Gateway: `192.168.3.1`
9. Verificar con **Probar conexion** usando `192.168.3.136`.
10. Anotar IP + numero de serie + MAC de cada lector. Si hay dos parantes, darles
    IPs distintas (`192.168.3.136` y `192.168.3.137`, por ejemplo).

## Notas

- Si el lector ya tiene otra IP, usar **Buscar lectores** estando en la misma red,
  o probar la IP conocida.
- El cambio de IP no se guarda hasta el corte de energia; avisar a quien corresponda
  antes de apagar el equipo.
- El programa del fabricante (`SReaderWPF.exe`, dentro de
  `libs/C#/Demo/Demo(3.5).rar`) tambien permite hacerlo, pero la interfaz esta en
  chino y puede requerir .NET Framework 3.5; esta herramienta evita ese problema.
- Para recompilar: ejecutar `build.ps1` en esta carpeta.
