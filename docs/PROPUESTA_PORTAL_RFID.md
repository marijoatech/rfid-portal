# Portal RFID de control de salida - Propuesta de diseno

Fecha: 16-10-2026
Estado: propuesta para revision (no hay todavia codigo de aplicacion en este repo; solo el SDK en `libs/`).

Contexto inicial dado por Marijoa:

- Se compraron portales RFID (barales/parantes de puerta) para control de entrada/salida de
  mercaderia; el foco es la salida.
- Cada parante tiene 4 antenas adentro. Hoy hay una sola barra conectada; los cables de
  la segunda barra vinieron cortos y se estan fabricando los largos (se conectaran en
  los puertos 5-8 del mismo lector E710 de 8 canales).
- Regla de negocio: **todo lo que pase por las antenas debe pertenecer a una
  `factura_venta` o a una `nota_remision`** (no deberia salir ningun EPC que no este en un
  documento).
- El deposito esta lleno de EPCs, por eso la prueba se hara en la puerta de salida
  (donde hay menos probabilidad de lecturas lejanas) y la potencia debe poder bajarse
  para calibrar.
- Hay PC cerca del porton (sistema hibrido posible) y pistolas BX6100 disponibles.
- Ya existen los modulos de control en `marijoa/remisiones` (ControlExpedicionRFID +
  ExpedicionLecturaRFID) y la app Android `rfid-handle-tools` (MarijoaRFID) para la pistola.

Decisiones ya tomadas (16-10-2026):

- Conexion **solo por red** (TCP/IP).
- **Descartados**: recepcion en porton y conteo/auditoria de zona (las piezas que llegan
  no traen RFID).
- La consola de calibracion sera **en vivo** (sin exportar CSV).
- Se quiere **loguear todo lo que sale** por el portal, en paralelo al control de
  sesiones.

---

## 1. Resumen ejecutivo

Con el lector fijo y las antenas se puede construir un **portero electronico de salida**:
un agente en una PC cercana lee continuamente las etiquetas que cruzan la puerta,
las convierte al formato de lote de Marijoa y las compara en tiempo real contra los
documentos habilitados (sesion de expedicion o remisiones/facturas abiertas).

Lo que se propone desarrollar, en orden de prioridad:

| # | Herramienta | Para que sirve | Cuando |
|---|---|---|---|
| A | **Portal de control de salida** (agente + integracion Marijoa) | Marca lo que pasa y alarma si un EPC no esta en los documentos de la sesion | Fase 1 (nucleo) |
| B | **Calibracion en Marijoa web (`PortalRFID`)** | Ver estado, potencias, antenas y etiquetas en vivo desde la pantalla que ya usa el personal | Fase 1 |
| B0 | **Consola tecnica de puesta en marcha** | Validar hardware, IP, potencias y fondo desde la PC del porton | Fase 0 (ahora, con 1 barra) |
| C | **Consultador de EPC** ("de quien es esta etiqueta") | Resolver sobrantes: dice en que documento abierto figura y donde esta | Fase 1/2 |
| D | ~~Recepcion en porton / conteo de zona~~ | **Descartados**: las piezas que llegan no traen RFID | - |
| E | **Extras** | Baliza/sirena por GPO, sensor de paso por GPI, LED de etiqueta (solo etiquetas Kiloway), barrera | Fase 2/3 |

La **consola WinForms (B0)** es solo para puesta en marcha y diagnostico tecnico. La UI
del personal sigue siendo Marijoa web: la pantalla nueva `PortalRFID` (B) consume la API
del agente y no obliga a nadie a aprender otra aplicacion.

Primer entregable recomendado: **B0** porque no depende de ningun cambio en Marijoa y
sirve ya con la barra conectada para responder: hasta donde lee, con cuanta potencia,
con que antena, cuantas etiquetas de fondo se ven y con que RSSI. Con esos numeros se
disena el resto.

---

## 2. Como funciona el lector (verificado contra el SDK y los manuales del fabricante)

### 2.1 Hardware y conexion

- El SDK entregado es **SReaderAPI**: `libs/C#/dll/SReaderAPI.dll` (.NET Framework 4.x) y
  `libs/java/Java/jar/SReaderAPI.jar`. Incluye ejemplos (ReadEpc, ReadEpcFilter, ReadTid,
  WriteEpc, ReaderGpioControl), el demo WPF con codigo fuente
  (`libs/C#/sourceCode/SReaderWPF-3.5.rar`) y demos Android.
- Conexion: **solo por red RJ45** (decision de Marijoa). `SReader.Create("tcp://IP")`;
  la IP de fabrica es 192.168.1.136. (La API tambien soporta serial RS232/USB
  `sld:///COMx` a 115200, pero no se usara.)
- **Cambio de IP del lector**: el SDK expone `GetIp()` y `SetIP(Ip_Info)` (IP, mascara y
  gateway) y el demo del fabricante ademas busca lectores en la LAN por **UDP broadcast
  (puerto 1500)**. El cambio de IP se aplica recien despues de **cortar y volver a dar
  energia** al lector (documentado por el fabricante). La consola de calibracion incluira
  este panel de red (ver actual, cambiar, buscar en la LAN).
- Primer contacto: si la red de Marijoa es 192.168.10.x, hay que conectar una PC con IP
  temporal 192.168.1.x (o usar la busqueda por broadcast) para cambiar la IP del lector a
  la red definitiva; conviene dejar **IP fija o reserva DHCP** documentada.
- La API soporta modelos M100, series R2000 (4/8/16 canales) y series E310/E510/E710.
  `GetReaderInfo().Type` dice el modelo y **cuantos canales/puertos tiene** (4, 8 o 16).
- Importante para el montaje: **GPO y GPI solo funcionan por TCP/IP** (no por serial),
  asi que la decision de ir por red es tambien requisito para baliza/sirena y sensor de
  paso.

### 2.2 Que permite el SDK (lo relevante para el portal)

| Funcion | API | Uso en el portal |
|---|---|---|
| Potencia general | `SetReaderPower(power)` | Ajuste rapido global (0-30 dBm segun serie; 0-26 en equipos de escritorio) |
| **Potencia por antena** | `SetPortReadPower(int[] antPower)` / `GetPortReadPower()` | **Clave para calibrar**: bajar/igualar potencia por puerto |
| Activar antenas | `SetAntenna(int[] antList)` | Elegir solo las antenas que miran a la puerta |
| Deteccion de antenas | `AntennaDetected()`, `CheckReturnLoss(ant)`, `SetReturnLoss(v)` | Saber si un cable/antena esta mal o desconectado |
| Red | `GetIp()`, `SetIP(Ip_Info)`, busqueda UDP (1500) | Panel de red de la consola: ver/cambiar IP y encontrar lectores |
| Inventario | `Inventry(Gen2.InventryValue(q, session), filtro)`, `Inventry_stop()` | Leer etiquetas continuamente o por ventanas |
| Inventario por antena/tiempo | `InventryValue(isTarget, target, ant, scanTime, q, session)` | Recorrer antena por antena en la calibracion |
| EPC + TID | `Inventry_mix(EmbedTagOp, filtro)` | Leer TID cuando haga falta identificar el chip |
| Tiempo de lectura | `SetScanTime(time)` (0-255 x100ms; 0 = sin limite) | Ventana de lectura por pasada |
| Q y Session | `InventryValue.q` (0-15, tipico 4), `session` (S0-S3) | S1 cuando hay muchas etiquetas; S0 cuando hay pocas |
| Filtro por mascara | `Gen2.Select(...)` | Ignorar etiquetas por prefijo, o leer solo un rango |
| Lectura por etiqueta | evento `TagRead` -> `EpcString`, `Ant`, `Rssi`, `Num` | Materia prima del control y de la calibracion |
| Salidas (GPO) | `SetGPO(id, high, seconds)` | Baliza, sirena, rele (pulso de N segundos) |
| Entradas (GPI) | `Gpi_Listeners_start()` + evento `GpioStatus` | Sensor de paso: leer solo cuando alguien cruza |
| Trigger por GPI | demo de fabricante: al cambiar GPI arranca lectura (5 s configurable) | Ventana de lectura automatica |
| Buzzer del lector | `SetBuzzer(0/1)` | Aviso sonoro local (si el equipo lo tiene) |
| Escritura | `WriteMemory`, `WriteEPC`, `LockTag`, `BlockErase` | Re-etiquetado / correccion (no es el objetivo del portal) |
| LED de etiqueta | `LightKLed(epc, ...)` | Solo etiquetas Kiloway con LED (futuro "pick to light") |
| Firmware | `FirmWare_Update(path)` | Actualizacion de firmware |

### 2.3 Limites que hay que asumir

1. **No hay sentido de paso (entrada/salida) por software**: esta API no expone fase ni
   Doppler. Se puede inferir con dos barras (orden de deteccion por antena) o con un
   sensor de paso por GPI, pero no es exacto. Para el objetivo ("no dejar salir nada que
   no este en un documento") el sentido no es imprescindible: alcanza con avisar que
   "algo paso y no esta en los documentos".
2. **RSSI es relativo** (0-100 aprox., no distancia): sirve para umbral y para
   comparaciones entre antenas, se calibra a mano.
3. **La lectura depende de la orientacion y del material**: rollos de tela van bien;
   apilados y encimados se tapan entre si. Lo que se lee bien es la pieza que cruza el
   plano de la puerta con cierta separacion.
4. **Un deposito lleno de etiquetas** significa que a 26-30 dBm el lector "ve" todo lo que
   este en linea de vista. Las armas para eso son: bajar potencia, usar solo 1-2 antenas
   por barra, umbral de RSSI, ventana de lectura corta por GPI, y una lista de "fondo"
   (EPCs fijos que siempre se ven y no se quieren reportar).

---

## 3. Como encaja con lo que ya existe en Marijoa

- **Sesion de expedicion**: `exp_sesion` (codigo de 6 digitos), `exp_transporte`,
  `exp_transporte_rem` (remisiones), `exp_sesion_fact` (facturas) y `exp_lectura`
  (EPC/lote leidos, `UNIQUE (id_sesion, lote)` -> el reenvio es inofensivo).
- **Pantalla PC**: `remisiones/ControlExpedicionRFID.*` arma la sesion, muestra el bloque
  por documento, puntea lineas, marca faltantes/sobrantes ("ajenas") y resuelve donde
  esta cada lote ajeno (documento abierto + ubicacion en `stock`).
- **Pantalla del lector**: `remisiones/ExpedicionLecturaRFID.*` es la que hoy usa la
  pistola (textarea + resumen + reiniciar/enviar contra `exp_lectura`).
- **Pistola BX6100**: app Android `rfid-handle-tools` (MarijoaRFID) + API
  `marijoa/rfid/api` con token de sesion. La pistola usa la regla:
  **EPC = lote solo digitos, rellenado con ceros a la izquierda hasta 24 caracteres
  hexadecimales (96 bits)**; por eso `lote 12820826 -> EPC 000000000000000012820826`.
  En el lector fijo la lectura llega como `EpcString` (hex) y el agente debe aplicar la
  misma regla: quitar ceros a la izquierda y validar que sean solo digitos.
- **El portal no reemplaza a la pistola**: el portal hace el control masivo y continuo en
  la puerta; la pistola queda para (a) repasar faltantes al final, (b) revisar un bulto
  ya armado, (c) buscar una pieza, y (d) como respaldo si el portal no puede leer algo.

### 3.1 Piezas nuevas propuestas en Marijoa (aditivas, para revisar antes de ejecutar)

Script `db/20261016_portal_rfid.sql` (solo CREATE TABLE + permisos, idempotente):

- `exp_portal`: configuracion por porton (nombre, suc, IP del lector, puerto, antenas
  activas, potencia por antena, umbral RSSI, Q, session, scanTime, lista de fondo,
  estado Activo/Inactivo, sesion armada).
- `exp_portal_comando`: cola de comandos para el agente (`INICIAR`, `DETENER`,
  `CONFIG`, `APRENDER_FONDO`, `ASIGNAR_SESION`), con estado
  `Pendiente/Entregado/Ejecutado/Error` y resultado.
- **El log va a `rfid_hist`** (decision de Marijoa), reutilizando
  `RFIDHistorialServicio::registrar()` para no duplicar SQL ni formato. Mapeo propuesto:
  - `accion`: `SALIDA_OK` (EPC en la sesion armada) / `SALIDA_SIN_DOC` (no esta en
    ningun documento abierto) / `SALIDA_OTRO_DOC` (esta en otro documento abierto).
  - `modulo`: `PORTAL_RFID`; `suc`: sucursal; `usuario`: usuario de la sesion, o
    `PORTAL` si es vigilancia sin sesion.
  - `codigo`: codigo de articulo resuelto desde `lotes`; `lote`: lote; `descripcion`:
    articulo + color (misma consulta que ya usa la app).
  - `epc_anterior`: EPC leido (unico campo EPC disponible); `epc_nuevo`: NULL.
  - `observacion`: portal, codigo de sesion, antena, RSSI y documento encontrado
    (ej. `PORTON-SALIDA-1 | sesion 144214 | ant 2 | rssi 61 | remision 119617`).
- **Dedupe del log**: una fila por EPC y por ventana de paso (por defecto 60 s,
  configurable), para no inundar `rfid_hist` con la misma etiqueta leida cientos de
  veces. La sesion sigue deduplicando por `exp_lectura (id_sesion, lote)`.
- No se crean tablas nuevas de lecturas ni de eventos: `rfid_hist` cumple ese rol.

API para el agente: `marijoa/rfid/api/portal.php` (mismo patron que `grabaciones.php`:
token + JSON, sin HTML). El agente **solo hace llamadas salientes** a Marijoa; no
necesita abrir puertos en la PC del porton. Acciones minimas:

- `estado`: devuelve si hay sesion armada, su codigo, la lista de lotes esperados y el
  perfil de calibracion vigente.
- `comandos`: devuelve los comandos pendientes para el porton y permite confirmar su
  ejecucion. Es el canal por el que la pantalla web de Marijoa inicia/detiene lecturas,
  cambia potencias/antenas y dispara el aprendizaje de fondo.
- `lecturas`: recibe un lote de lecturas del agente (`epc`, `lote`, `ant`, `rssi`,
  `fecha`) y las inserta en `exp_lectura` con `INSERT IGNORE`, devolviendo el resumen
  (punteadas / faltantes / ajenas). Ademas registra el paso en `rfid_hist` (con dedupe
  por ventana).
- `evento`: registra una alarma o una resolucion de sobrante.

La logica de negocio sigue viviendo en Marijoa (una sola fuente de verdad); el agente
solo hace hardware, filtros locales y feedback.

---

## 4. Arquitectura propuesta

```
┌───────────────────────────────────────────────────────────────┐
│                    Marijoa web (PHP + MySQL)                  │
│  UI: ControlExpedicionRFID          UI: PortalRFID            │
│  API: /marijoa/rfid/api/portal.php                            │
│  DB: exp_sesion*, exp_lectura, exp_portal*, rfid_hist         │
└──────────────▲─────────────────────────────┬─────────────────┘
               │ HTTP/JSON (lecturas/estado)  │ HTTP/JSON (comandos/config)
               │                             ▼
┌──────────────┴───────────────────────────────────────────────┐
│              Portal Agent (servicio Windows)                 │
│  - conecta al lector por TCP/IP                              │
│  - aplica potencia/antenas y lee EPCs                        │
│  - filtra, deduplica y registra                              │
└──────────────────────────────▲───────────────────────────────┘
                               │ TCP/IP (SDK SReader)
                     ┌─────────┴──────────┐
                     │ Lector UHF E710    │
                     │ 8 canales          │
                     │ 4 antenas/parante  │
                     └────────────────────┘
```

- **Marijoa web** es la UI del personal: la sesion, el control y la calibracion se
  manejan desde sus pantallas (misma sesion y permisos actuales).
- **Portal Agent** es un servicio Windows headless: solo hardware, filtros y
  comunicacion con la API. No es una aplicacion para el personal.
- **Consola tecnica WinForms**: herramienta de puesta en marcha en la PC del porton;
  valida IP/ROE/potencias/lectura antes de conectar la web. No reemplaza las pantallas
  de Marijoa.
- **Sin puertos entrantes en el porton**: el agente consulta comandos y publica
  lecturas/estado (siempre por salida). La web nunca habla directo con el lector.

---

## 5. Flujos

### Flujo 1 - Salida con sesion (recomendado)

1. El operador arma la sesion en Marijoa web como hoy: elige remisiones/facturas, se
   genera el codigo de 6 digitos y el QR.
2. El operador asigna el porton a esa sesion (selector en la pantalla de control). La
   API entrega al agente la sesion armada, la lista de EPCs esperados y el perfil de
   calibracion.
3. A medida que pasan las piezas por la puerta, el agente lee EPCs, aplica filtros
   (antena/umbral/ventana/fondo), normaliza EPC->lote y los envia a Marijoa.
4. Marijoa marca las lineas en `exp_lectura` y devuelve el estado (punteadas, faltantes,
   ajenas). La pantalla web de Marijoa se actualiza en vivo.
5. Si pasa un EPC que **no** esta en los documentos de la sesion: alarma inmediata
   (sonido/baliza) y queda listado como sobrante con "en que documento abierto figura"
   y su ubicacion actual.
6. Al terminar: si estan todas las piezas y no hay sobrantes -> "Enviar" como hoy. Si
   faltan piezas -> repaso con la pistola (hibrido). Si hay sobrantes -> se resuelve cada
   uno (retirar la pieza, o agregar el documento que la contiene a la sesion) y se
   reinicia el escaneo.
7. Cierre de sesion.

### Flujo 2 - Vigilancia continua y registro permanente (en paralelo)

1. El portal lee **siempre**, haya o no sesion armada.
2. **Todo** paso por la puerta se registra en `rfid_hist` (accion `SALIDA_*`,
   modulo `PORTAL_RFID`): EPC, lote, articulo, antena, RSSI y fecha; si habia sesion
   armada, tambien su codigo.
3. Si hay sesion armada, ademas del log se envia a `exp_lectura` y se puntea la sesion
   como en el Flujo 1: **los dos caminos corren a la par** (loguear no reemplaza el
   control, y controlar no reemplaza el log).
4. Sin sesion, el sistema clasifica cada EPC: pertenece a un documento abierto (remision
   o factura, con numero y sucursal) o es desconocido; opcionalmente dispara alarma.
5. El log es la red de seguridad: aunque nadie armo sesion, queda registro de todo lo que
   paso (quien, cuando, con que RSSI y en que antena) para revisar despues.

### Flujo 3 - Calibracion (Fase 0)

1. Abrir `PortalRFID` en Marijoa (o la consola tecnica si la web aun no esta) con la
   puerta **vacia**.
2. Ver estado del equipo (modelo, canales, version, antenas detectadas, ROE por antena).
3. Encender antena por antena y medir cuantas etiquetas de fondo aparecen y con que RSSI.
4. Bajar potencia (26 -> 24 -> 22... dBm) por antena hasta que solo se lea la puerta.
5. Guardar la lista de fondo (EPCs fijos) y el umbral de RSSI.
6. Probar con una pieza: distancia y orientacion a la que se lee con seguridad.
7. Guardar el perfil (antenas, potencias, umbral, Q, session, scanTime) como perfil del
   porton.

### Flujo 4 - Excepcion: sobrante ("ajena")

1. Suena la alarma y la pantalla muestra el EPC/lote.
2. El operador usa el consultador (o el detalle de ajenas que ya existe) para ver en que
   documento abierto figura y donde esta.
3. Opciones: (a) retirar la pieza de la zona de salida, (b) agregar ese documento a la
   sesion si realmente sale, (c) registrar la observacion y continuar.
4. Política a definir con Marijoa: si un sobrante documentado debe o no bloquear el
   cierre de la sesion (hoy bloquea; se propone distinguir "sobrante resuelto" de
   "sobrante pendiente" para no trabar el envio).

### Flujo 5 - Hibrido con pistola

1. El portal marca el 100% de lo que paso.
2. Si quedan faltantes (piezas que no cruzaron o no se leyeron), el operador toma la
   pistola y repasa el bulto/pallet (la app ya tiene Control y Buscar).
3. Si la pistola detecta una pieza que el portal no vio, se envia igual (mismo
   `exp_lectura`) y la sesion queda completa.

---

## 6. Herramienta B: calibracion en Marijoa + consola tecnica

**UI del personal**: nueva pantalla `remisiones/PortalRFID.*` en Marijoa
(PHP/HTML/JS/CSS con el sistema `.mj-*`), con estado del lector, antenas/potencias,
lectura en vivo, aprendizaje de fondo y perfiles. La pantalla escribe comandos en
`exp_portal_comando` y consulta `estado` cada 1-2 s.

**Consola tecnica**: `src/PortalCalibration/` (WinForms) para puesta en marcha y
diagnostico en la PC del porton. No es la interfaz operativa del personal.

Ya disponible en este repo: `tools/PortalIpTool/` (ejecutable + `SReaderAPI.dll` +
`LEEME.md`) para que la notebook lea/cambie la IP del lector y busque equipos en la LAN
sin instalar nada.

Funciones minimas (la web y la consola comparten la misma API/configuracion):

- Conectar/desconectar al lector por **red** (IP configurable; soporte para 2 lectores a
  futuro).
- **Panel de red**: ver la IP/mascara/gateway actuales (`GetIp`), asignar una IP nueva
  (`SetIP`, requiere reiniciar el lector) y buscar lectores en la LAN (UDP broadcast
  puerto 1500).
- Estado del equipo: modelo/canales (`GetReaderInfo`), version, region, numero de serie,
  antenas detectadas (`AntennaDetected`) y ROE por antena (`CheckReturnLoss`).
- Potencia: global y **por antena** (`SetPortReadPower`), con lectura de la potencia
  actual (`GetPortReadPower`).
- Antenas: activar/desactivar (`SetAntenna`).
- Inventario **en vivo** (sin exportar nada): EPC, lote normalizado, antena, RSSI,
  cantidad de lecturas, primera/ultima vez visto; filtros, Limpiar y pausa. Todo se mira
  en pantalla.
- Filtro por umbral de RSSI y lista de fondo (aprender fondo con la puerta vacia y
  guardarlo).
- Parametros de inventario: Q, session, scanTime, modo (todas las antenas / antena
  seleccionada / ciclado por antena).
- Prueba de GPO (encender baliza N segundos) y buzzer, con el lector por red.
- Perfiles: guardar/cargar por porton (archivo local, y mas adelante en `exp_portal`).
- Simulador: permitir inyectar EPCs a mano para probar la comparacion sin hardware.

Con esto ya se pueden responder las preguntas tecnicas de la instalacion actual
(1 barra) y preparar los numeros para la Fase 1.

---

## 7. Herramienta A: portal de control de salida (nucleo)

- Agente leyendo de forma continua con ventanas cortas (para no saturar) y filtros:
  antenas activas + umbral RSSI + lista de fondo + dedupe por tiempo (una etiqueta no se
  reenvia antes de N segundos).
- Envio por lotes a Marijoa (cada 1-2 s o inmediato si es un EPC nuevo) con ant/rssi.
- **Canal de comunicacion**: el agente consulta `portal.php?action=comandos` cada 1-2 s
  y publica `lecturas`/`estado`; no abre puertos entrantes en la PC del porton.
- **Armado de la sesion del portal**: cada porton tiene identidad propia (ej.
  `PORTON-SALIDA-1`), configurada en `exp_portal` y en el agente. Al armar la sesion en
  Marijoa web (pantalla de control de siempre) se elige el porton que la controla; el agente consulta
  `portal.php?action=estado&portal=...` cada 1-2 s y recibe `{id_sesion, codigo}`. El
  agente nunca "adivina": si no hay sesion armada para ese porton, solo loguea.
- **Log permanente en paralelo**: cada paso se registra en `rfid_hist` (con o sin
  sesion); el envio a `exp_lectura` es adicional cuando hay sesion armada.
- **Respaldo manual**: campo de codigo de 6 digitos en la pantalla web (o consola
  tecnica) para forzar la sesion, mismo efecto que tipear el codigo en la pistola.
- Feedback local: pantalla (opcional), sonido, baliza por GPO, y mensaje en la pantalla
  de control.
- Regla de alarma configurable: cualquier EPC fuera de los documentos de la sesion;
  opcionalmente "solo si el EPC pertenece a un documento cerrado/inexistente" vs
  "pertenece a otro documento abierto" (distinto tono/color).
- Trazabilidad de alarmas: cada sobrante/aviso queda en `rfid_hist` con accion
  `SALIDA_SIN_DOC` o `SALIDA_OTRO_DOC` (la observacion indica el detalle).
- Sin cambios en la operacion actual: la sesion se sigue armando y cerrando en
  ControlExpedicionRFID.

---

## 8. Plan por fases

| Fase | Alcance | Entregable | Depende de |
|---|---|---|---|
| 0 | Consola tecnica + calibracion de hardware con la barra conectada | `PortalCalibration` (conectar, potencias, lectura en vivo, fondo) | Lector/IP (ya) |
| 1 | Agente de servicio + API + pantalla `PortalRFID` en Marijoa | Comandos/estado, configuracion y lectura en vivo dentro de Marijoa | Fase 0 + URL/token de Marijoa |
| 2 | Modo sesion completo + alarmas + segundo parante | Punteo de sesiones, GPO/GPI, las 8 antenas | Fase 1 + cables 5-8 |
| 3 | Herramientas de soporte | Consultador de EPC y reportes del log | Fase 2 estable |

---

## 9. Datos y decisiones que necesitamos de Marijoa

1. **Confirmado**: lector E710 de 8 canales en `192.168.3.60`; antenas 1-4 conectadas y
   las 5-8 al llegar los cables largos en fabricacion.
2. **URL base de Marijoa** desde la PC del porton (HTTP/HTTPS) y token de API para el
   agente.
3. **PC del porton**: hay una PC que pueda quedar encendida y con red al lector?
   (puede ser la misma PC de la pantalla de control).
4. **Politica de sobrantes**: bloquea el cierre de la sesion o solo avisa? Se quiere
   "sobrante resuelto" vs "pendiente"?
5. **Alarma fisica**: existe baliza/sirena/rele disponible? Va por GPO del lector o por
   la PC?
6. **Alcance de documentos**: ademas de factura_venta y nota_remision, hay otros casos
   (transferencias, devoluciones, retiros de taller) que deban considerarse "documento
   valido" para salir?
7. **Etiquetado**: todas las piezas que salen por el porton tienen etiqueta con el lote
   como EPC? (lo que salga sin etiqueta no lo puede ver el portal).
8. **Ventana de dedupe del log** en `rfid_hist`: 60 s por EPC esta bien, o prefieren otro
   valor?
9. **Permisos de `PortalRFID`**: usar los mismos de expedicion (8.6/8.7) o crear un
   permiso nuevo para calibracion/administracion del porton?

---

## 10. Anexo: llamadas clave del SDK (C#) y regla EPC -> lote

```csharp
// Conexion (red)
SReader reader = SReader.Create("tcp://192.168.1.136");
reader.Connect();

// Antenas y potencia por antena (4 canales)
reader.SetAntenna(new int[] { 1, 2, 3, 4 });
reader.SetPortReadPower(new int[] { 22, 22, 18, 18 });
reader.SetCheckAnt(1);                 // deteccion de antenas activa
reader.SetScanTime(5);                 // ventana de 500 ms por consulta

// Escucha de etiquetas
reader.TagRead += (s, e) => {
    string epc = e.TagData.EpcString;  // hex, 24 chars tipicamente
    int ant    = e.TagData.Ant;
    int rssi   = e.TagData.Rssi;
    // lote = epc sin ceros a la izquierda, solo digitos
};

reader.Inventry(new Gen2.InventryValue(4, 1), null);   // Q=4, Session=S1
// ...
reader.Inventry_stop();

// Salidas/entradas (solo con conexion TCP/IP)
reader.SetGPO(1, 1, 3);                // GPO1 en alto 3 segundos
reader.Gpi_Listeners_start();

reader.ShutDown();
```

Regla de conversion ya usada por la pistola y que el agente debe respetar:

```
EPC leido : 000000000000000012820826
lote      : 12820826
```

(Solo digitos; se descartan ceros a la izquierda; si el EPC no es solo digitos, se
reporta como etiqueta invalida/ajena.)
