# PortalAgent - modo consola para pruebas

Agente intermedio entre los lectores fijos y Marijoa. Esta version corre en
consola para probar en la PC actual; luego se instala como servicio de Windows.

## Archivos

- `PortalAgent.exe`
- `SReaderAPI.dll`
- `Newtonsoft.Json.dll`
- `agent.json`

`agent.json` debe tener la URL de Marijoa, el token del agente y la lista de
lectores que se quieren probar:

```json
{
  "Agente": "AGENTE-PORTONES-1",
  "MarijoaUrl": "http://localhost/marijoa/rfid/api/portal.php",
  "Token": "...",
  "Lectores": ["192.168.3.60"],
  "PollSegundos": 2,
  "DedupeSegundos": 60,
  "LogEpcs": false,
  "ResumenLogSegundos": 60,
  "FondoSegundos": 180,
  "FondoSilencioSegundos": 0,
  "LogArchivo": "logs/portal-agent.log"
}
```

Para la PC de prueba se deja solo `192.168.3.60`; los lectores `.61` a `.63`
se agregan cuando esten instalados. Si `Lectores` queda vacio, el agente intenta
todos los lectores que devuelve Marijoa.

## Ejecutar

1. Cerrar o desconectar `PortalCalibration.exe` del mismo lector.
2. Abrir una consola (CMD/PowerShell) en la carpeta `dist`.
3. Ejecutar:

```text
PortalAgent.exe
```

El agente consulta Marijoa cada 2 segundos, conecta el lector, lee y envia.
Con `Ctrl+C` se detiene.

Por defecto `LogEpcs` es `false`: el log escribe un resumen por portal cada 60
segundos con cantidades enviadas/guardadas/repetidas y los ultimos 3 lotes, en
lugar de una linea por cada EPC. Para diagnostico detallado se puede usar
`LogEpcs: true`; en ese caso tambien registra EPC, lote, antena y RSSI de cada
lectura aceptada. El historial completo de pasos sigue en `rfid_hist`, no en el
archivo de texto. El log rota a los 5 MB y conserva un archivo anterior
(`portal-agent.log.1`).

La potencia se calibra desde Marijoa: **Portales RFID > Potencia general**. El
control cambia los 8 puertos del lector en conjunto, sin tener que ajustar
antena por antena. El agente confirma el comando solo despues de aplicar la
potencia al lector.

## Entrenar el fondo

1. Dejar la puerta **vacia** (sin piezas en transito).
2. Verificar que `PortalAgent.exe` este corriendo.
3. Ejecutar `Aprender-fondo.cmd`.
4. El agente guarda todos los EPC detectados, incluidos los vistos una sola vez. El aprendizaje continua hasta
   presionar **Terminar aprendizaje**, con un maximo de seguridad de 180 segundos.
5. Desde ese momento esos EPC no se envian a Marijoa.

El fondo no bloquea una pieza que luego sale en un documento: si un EPC
aprendido como fondo pertenece a la sesion armada, se acepta igual. El fondo
solo descarta las lecturas que no estan en ningun documento de esa sesion.
Si se mueve la zona y cambia el fondo, ejecutar Limpiar-fondo.cmd y volver a
entrenar con la puerta vacia.

Para borrar el fondo aprendido y volver a entrenar:

```text
Limpiar-fondo.cmd
```

No se puede aprender fondo con una sesion armada. Si la puerta no estaba
vacia, limpiar el fondo y repetir el entrenamiento.

Si no hay una sesion armada en Marijoa, las lecturas se registran como
`LECTURA_PORTAL` en `rfid_hist`. Si hay una sesion armada para Expedicion, se
insertan ademas en `exp_lectura` y puntean la sesion.

## Importante

- La consola de calibracion y el agente no deben usar el mismo lector al mismo
  tiempo.
- Para produccion se instalara como servicio de Windows; esta version consola
  es solo para la prueba en la PC actual.
