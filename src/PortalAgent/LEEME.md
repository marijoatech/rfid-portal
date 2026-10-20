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

Con `LogEpcs: true` en `agent.json`, el log muestra una linea por lectura
aceptada con EPC, lote, antena y RSSI. No registra las etiquetas de fondo ni
las repeticiones dentro de la ventana de dedupe. El log rota a los 5 MB y
conserva un archivo anterior (`portal-agent.log.1`).

## Entrenar el fondo

1. Dejar la puerta **vacia** (sin piezas en transito).
2. Verificar que `PortalAgent.exe` este corriendo.
3. Ejecutar `Aprender-fondo.cmd`.
4. El agente aprende durante 12 segundos los EPC vistos 2 o mas veces y los
   guarda en `exp_portal_fondo`.
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
