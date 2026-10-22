# rfid-portal

Portal RFID de control de salida (parantes con antenas UHF) para Marijoa.

La interfaz que sigue usando el personal es **Marijoa web**. Este repositorio contiene
el componente de hardware (agente Windows) y las herramientas de puesta en marcha; la
operacion y la configuracion se muestran desde Marijoa a traves de APIs.

## Arquitectura del sistema

```text
┌───────────────────────────────────────────────────────────────┐
│                    Marijoa (PHP + MySQL)                      │
│                                                               │
│  UI: ControlExpedicionRFID          UI: PortalRFID            │
│      (sesiones, bloques, sobrantes)    (calibracion/estado)   │
│                                                               │
│  API: /marijoa/rfid/api/portal.php                            │
│      estado | comandos | lecturas | evento                    │
│                                                               │
│  DB: exp_sesion*, exp_lectura, exp_portal*, rfid_hist         │
└──────────────▲─────────────────────────────┬─────────────────┘
               │ HTTP/JSON                   │ HTTP/JSON
               │ lecturas + estado           │ comandos + config
               │                             ▼
┌──────────────┴───────────────────────────────────────────────┐
│              Portal Agent (servicio Windows)                 │
│  - conecta al lector por TCP/IP                              │
│  - aplica potencia/antenas y lee EPCs                        │
│  - filtra, deduplica y registra                              │
│  - pantalla/log solo para diagnostico tecnico                │
└──────────────────────────────▲───────────────────────────────┘
                               │ TCP/IP (SDK SReader)
                     ┌─────────┴──────────┐
                     │ Lector UHF E710    │
                     │ 8 puertos          │
                     │ 2 barras x 4 = 8   │
                     └────────────────────┘
```

### Componentes

| Componente | Donde corre | Responsabilidad |
|---|---|---|
| **Marijoa web** | Servidor Marijoa | UI del personal: crear/armar sesiones, control de expedicion, estado y calibracion. Fuente de verdad del negocio. |
| **API RFID** (`marijoa/rfid/api/portal.php`) | Servidor Marijoa | Recibe lecturas/estado del agente y entrega la configuracion y los comandos pendientes. |
| **Portal Agent** | PC del porton | Servicio Windows sin interfaz de usuario operativa: habla con el lector por TCP/IP, aplica potencia/antenas y publica las lecturas. |
| **Lector UHF** | Una puerta | E710 de 8 canales por puerta; 8 antenas por lector (2 barras x 4). Hoy el de Expedicion tiene los puertos 1-4 conectados; los 5-8 se conectaran cuando esten los cables largos. |
| **Pistola BX6100 + MarijoaRFID** | Android | Repaso de faltantes, busqueda de piezas y respaldo del portal. |
| **Consola de calibracion** | PC del porton | Herramienta tecnica de puesta en marcha (WinForms). No reemplaza la UI del personal. |

### Flujo de integracion

1. El operador trabaja en **Marijoa web**: crea/arma la sesion de expedicion, elige los
   documentos y asigna el porton.
2. La pantalla web deja la configuracion/comandos en Marijoa (tabla `exp_portal` /
   `exp_portal_comando`).
3. El **Portal Agent** consulta la API cada 1-2 segundos (siempre por salida hacia
   Marijoa), toma los comandos pendientes y los aplica al lector.
4. El agente lee los EPCs que cruzan la puerta, los normaliza a lote y publica las
   lecturas y su estado en la API.
5. Marijoa escribe el punteo de la sesion en `exp_lectura` y el paso del portal en
   `rfid_hist` (acciones `SALIDA_OK`, `SALIDA_SIN_DOC`, `SALIDA_OTRO_DOC`).
6. La pantalla de control y la pantalla `PortalRFID` muestran el resultado en vivo.

### Reglas de comunicacion

- **Sin puertos entrantes en la PC del porton** por defecto: el agente solo hace
  peticiones salientes al servidor Marijoa. Asi funciona aunque el porton este detras
  de un firewall/NAT.
- Si mas adelante se necesita un panel tecnico local, puede exponerse una API HTTP
  solo en la LAN, con token y sin salida a Internet.
- La UI operativa del personal **no habla directo con el lector**: siempre pasa por
  Marijoa y su API.
- El lector debe ir por red: **GPO/GPI (baliza, sirena, sensor de paso) solo funcionan
  por TCP/IP**. El puerto TCP lo maneja el SDK SReader.

### ¿Donde corre el Portal Agent?

| Opcion | Recomendacion | Motivo |
|---|---|---|
| Mini PC Windows fija en el porton, en la misma LAN del lector (`192.168.3.x`) | **Recomendada** | Queda encendida, con Ethernet y sin suspender; alcanza al lector y a Marijoa. |
| PC existente de `ControlExpedicionRFID` | Aceptable | Si queda encendida y con red; el agente corre como servicio en segundo plano. |
| Notebook | Solo para puesta en marcha | Se suspende, se mueve y no esta pensada para quedar como servidor. |
| Servidor Marijoa Linux | No con este SDK | `SReaderAPI.dll` es .NET Framework (Windows); habria que hacer otra implementacion. |

Requisitos del equipo: Windows 10/11 x64, .NET Framework 4.x (viene incluido), puerto
Ethernet, IP fija o reserva DHCP y encendido durante toda la operacion. No necesita
monitor ni teclado en produccion.

### Hardware de la PC del agente

El trabajo pesado lo hace el lector; la PC solo recibe eventos, filtra y hace unas pocas
llamadas HTTP por segundo. **No necesita un procesador potente ni placa de video.**

| Componente | Minimo | Recomendado |
|---|---|---|
| Procesador | x64 de 2 nucleos, 1.5 GHz o superior | Intel N100/N150, i3 de 8va o superior, Ryzen 3, o similar (4 nucleos) |
| Memoria RAM | 4 GB | 8 GB |
| Disco | 64 GB SSD/eMMC | 128-256 GB SSD |
| Red | 1 puerto Ethernet | Ethernet Gigabit por cable |
| Sistema | Windows 10 x64 (funciona, pero ya sin soporte) | **Windows 11 Pro x64** |
| Energia | Fuente estable | Mini PC fanless y, si se quiere, UPS + autoencendido tras corte |

El agente va **dedicado**: sin monitor, sin teclado y sin navegador. Con un Intel N100,
8 GB y SSD de 128 GB sobra; incluso 4 GB alcanzan para solo el agente. No hace falta un
i7 ni una PC gamer. Conviene elegir un equipo de marca o industrial fanless (Lenovo Tiny,
HP Mini, Dell Micro, Intel NUC, ASUS PN, etc.) y evitar notebooks, discos eMMC muy
chicos, Windows de 32 bits o Windows on ARM.

El agente **inicia la conexion TCP hacia el lector** (el lector es el servidor TCP); no
hay que abrir un puerto entrante en la PC. La pantalla para el personal puede abrirse
desde cualquier PC que acceda a Marijoa; el unico requisito es que la PC del agente
tenga salida HTTP/HTTPS al servidor Marijoa y alcance `192.168.3.60`.

### Operacion diaria y calibracion

- **Produccion**: el `Portal Agent` corre como servicio de Windows, arranca con el
  equipo y se controla desde Marijoa. Nadie tiene que abrir `PortalCalibration.exe`
  ni apretar "Iniciar" todos los dias.
- **Calibracion**: `PortalCalibration.exe` se usa solo en puesta en marcha. Lee del
  lector y muestra los EPC en pantalla, pero **no envia nada a Marijoa**.
- No conectes la consola y el agente al mismo lector al mismo tiempo: el lector debe
  quedar libre para el agente. Para calibrar, detene el agente sobre ese lector o
  desconecta la consola antes de arrancarlo.
- El fondo aprendido filtra ruido no documentado, pero **no bloquea una pieza que
  despues sale en una sesion**. Si un EPC del fondo aparece en la sesion, se acepta;
  si cambia la zona, se limpia y se vuelve a entrenar.
- El aprendizaje usa la potencia real de operacion y continua hasta que el
  operador presiona **Terminar aprendizaje**, con un maximo de seguridad de 180
  segundos. Guarda todos los EPCs detectados, incluidos los vistos una sola vez.
- **Probar cobertura** es una captura temporal separada que incluye tambien los
  EPCs del fondo. Sirve para colocar rollos conocidos y comprobar cuales alcanza
  el portal sin modificar el fondo guardado.
- Sin sesion armada, un EPC fuera del fondo que no pertenece a ningun documento
  abierto activa luz roja y alarma sonora. La misma alerta no se repite durante
  5 minutos. El sonido se puede silenciar por portal sin desactivar la luz roja.
- La potencia de calibracion se ajusta desde **Portales RFID** en Marijoa. Un solo
  control cambia todos los puertos del lector; el agente aplica la orden al hardware
  y confirma el resultado. El punto inicial medido es 16-18 dBm.
- El archivo de texto del agente usa resumenes cada 60 segundos y conserva los
  ultimos lotes vistos. El detalle EPC por EPC queda en `rfid_hist`; `LogEpcs: true`
  se reserva para diagnostico y genera mucho mas volumen.

### Diagnostico de GPO / salidas externas

El SDK 3.7 expone cuatro salidas:

| GPO | Funcion |
|---|---|
| 1 | Luz roja |
| 2 | Luz amarilla |
| 3 | Luz verde |
| 4 | Parlante/alarma |

`Ejecutado` en Marijoa significa que el SDK del lector acepto el pulso GPO. No
demuestra por si solo que una sirena, baliza o rele externo haya quedado conectado.
La salida GPO del lector es una senal de baja tension; una sirena de 12/24/220 V no
debe conectarse directamente sin conocer su consumo y usar el rele o driver adecuado.

Si los tres GPO responden desde la web pero no ocurre nada fisicamente:

1. Confirmar en el lector cual bornera corresponde a GND y a GPO 1/2/3.
2. Probar con un pulso largo (30-60 s) y medir en DC entre GPO y GND.
3. Verificar que la sirena/baliza tenga su propia alimentacion y que el contacto del
   rele corte la linea correcta.
4. No puentear ni conectar 220 V al lector. Solicitar al fabricante el esquema
   electrico del modelo y la corriente maxima permitida por GPO.

### Puertas y lectores (4 puertas, 1 lector por puerta)

Cada puerta tiene **2 barras x 4 antenas = 8 antenas**, conectadas a un unico lector
E710 de 8 canales: un puerto por antena. No hay un lector por barra.

| Puerta | IP | Lector | Antenas | Estado actual |
|---|---|---|---|---|
| Expedicion | `192.168.3.60` | E710 8 canales | Puerto 1-4 barra A; 5-8 barra B | 1-4 conectadas; 5-8 al llegar los cables |
| Entrada 1 | `192.168.3.61` | E710 8 canales | Puerto 1-4 barra A; 5-8 barra B | A instalar |
| Entrada 2 | `192.168.3.62` | E710 8 canales | Puerto 1-4 barra A; 5-8 barra B | A instalar |
| Entrada Personal | `192.168.3.63` | E710 8 canales | Puerto 1-4 barra A; 5-8 barra B | A instalar |

Como funciona:

- El agente abre **una conexion independiente por puerta/lector** y mantiene un worker
  por lector; si uno se cae, reconecta y no frena a los demas.
- En Marijoa se configura, por cada lector, su IP, los 8 puertos de antena (activos o
  no) y la potencia de cada puerto. El agente recibe esa configuracion por la API.
- El agente consulta `AntennaDetected()` en cada lector para verificar que el puerto
  este fisicamente conectado; si la configuracion dice que la antena 3 esta activa y
  el lector no la detecta, lo informa en Marijoa y no la usa.
- Dentro de una misma puerta se deduplica el mismo EPC visto por varias de sus 8
  antenas. Puertas distintas no se mezclan: cada una usa su propia sesion.
- Si una sola PC no alcanza a todos los lectores, se puede correr un agente por zona;
  todos hablan con la misma API de Marijoa.

### Como recibe el agente las IPs

El agente no tiene las 4 IPs escritas a mano en el ejecutable. Solo guarda una
configuracion local minima:

```text
agente      = AGENTE-PORTONES-1
marijoaUrl  = http://192.168.10.10/marijoa
token       = <token del usuario de servicio>
```

La lista de lectores vive en Marijoa, en `exp_portal_lector`:

| Portal | IP |
|---|---|
| EXPEDICION | 192.168.3.60 |
| ENTRADA1 | 192.168.3.61 |
| ENTRADA2 | 192.168.3.62 |
| PERSONAL | 192.168.3.63 |

En cada heartbeat, la API le devuelve al agente esa lista. El agente abre una conexion
por IP y mantiene un worker independiente. Si se agrega o cambia una IP en Marijoa,
el agente lo detecta en la siguiente consulta, cierra la conexion anterior y abre la
nueva; si un lector no responde, reintenta solo ese lector sin afectar a los otros.

Para tolerar un corte de Marijoa, el agente puede guardar la ultima lista buena en un
archivo local (`readers-cache.json`) y seguir leyendo/logueando hasta recuperar la API.
La base de Marijoa sigue siendo la fuente de verdad.

### Redes y ubicacion

| Red | Uso actual | Ejemplo |
|---|---|---|
| `192.168.3.x` | Lectores y PCs de usuarios | Lector `192.168.3.60` |
| `192.168.10.x` | Servidor Marijoa | `192.168.10.10` |
| `192.168.2.x` | Equipos de desarrollo/oficina | Esta PC: `192.168.2.80` |

La PC del agente debe alcanzar **las IPs de los lectores** (TCP) y **Marijoa**
(HTTP/HTTPS). La ubicacion ideal es la red de los lectores (`192.168.3.x`), con salida
hacia `192.168.10.10`. Tambien puede estar en `192.168.2.x` si el ruteo y el firewall
permiten esos dos destinos; desde esta PC comprobamos ping a `192.168.3.60` y a
`192.168.10.10`.

Reglas de firewall:

- Agente -> lectores: TCP (puerto del SDK SReader).
- Agente -> Marijoa: HTTP/HTTPS a `192.168.10.10`.
- Usuarios de `192.168.3.x` -> Marijoa: HTTP/HTTPS (ya funciona hoy).
- No hace falta abrir ningun puerto entrante en la PC del agente.

### Datos y trazabilidad

| Dato | Tabla | Uso |
|---|---|---|
| Sesiones/expedicion | `exp_sesion`, `exp_transporte*`, `exp_sesion_fact` | Documentos y transporte de la sesion |
| Punteo de sesion | `exp_lectura` | `UNIQUE (id_sesion, lote)`; el reenvio es inofensivo |
| Configuracion del porton | `exp_portal` | IP, potencias, antenas, umbral, fondo, sesion armada |
| Comandos del agente | `exp_portal_comando` | Cola de comandos (iniciar/detener/configurar/aprender fondo) |
| Log de pasos | `rfid_hist` | Una fila por paso/EPC (con dedupe por ventana), usando el servicio existente |

## Estado actual (20-10-2026)

- Primer lector (puerta de **Expedicion**) configurado en **192.168.3.60**; responde
  ping y conexion TCP por el SDK.
- Plan de 4 puertas/lectores: Expedicion `.60`, Entrada 1 `.61`, Entrada 2 `.62`,
  Entrada Personal `.63` (8 antenas por lector).
- Equipo detectado: **E710 de 8 canales** (`Type 0x76`), firmware **2.8**, serie
  **26027126**, region **US**, potencia **20 dBm** en los 8 puertos.
- Antenas detectadas: **1, 2, 3 y 4**. Los puertos 5-8 quedan libres porque los cables
  vinieron cortos; se estan fabricando cables mas largos para conectar la segunda barra.
- Prueba rapida de lectura a 20 dBm: 91 etiquetas distintas en pocos segundos; hay
  bastante fondo en la zona, por eso la calibracion es central.
- Prueba de potencia (antenas 1-4, 4 segundos): 20 dBm = 91 etiquetas; 16 dBm = 15
  etiquetas; 12 dBm = 0 etiquetas. La potencia se restauro a 20 dBm al terminar.
  Punto de partida sugerido para calibrar con piezas reales: 16-18 dBm.
- Herramienta de IP para la notebook: `tools/PortalIpTool/`.
- Consola de calibracion/banco de pruebas: lista en `src/PortalCalibration/`
  (`PortalCalibration.zip`). Es de un lector; el agente de produccion sera multi-lector.
- API de agente y modo consola de prueba: `marijoa/rfid/api/portal.php` y
  `src/PortalAgent/`. En la prueba local solo se conecta a `.60`; todavia no es
  un servicio de Windows.

## Documentacion

- Propuesta completa y flujos: [docs/PROPUESTA_PORTAL_RFID.md](docs/PROPUESTA_PORTAL_RFID.md)
- Herramienta de IP: [tools/PortalIpTool/LEEME.md](tools/PortalIpTool/LEEME.md)
- SDK del fabricante: `libs/` (C# y Java, con demos y manuales)
