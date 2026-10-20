-- Apply MARIJOA_RULES.md strictly.
-- Portales RFID - datos iniciales de las 4 puertas.
-- Compatible con MySQL 5.5/5.7 y MariaDB 5.5.
-- Requiere haber ejecutado 20261020_portal_rfid_tablas.sql.
--
-- CAMBIAR @suc_portal si la sucursal no es 00.
--   mysql --default-character-set=latin1 -u USUARIO -p BASE < 20261020_portal_rfid_datos.sql

SET NAMES 'latin1';
SET @suc_portal := '00';


INSERT IGNORE INTO exp_portal (codigo, nombre, suc, tipo, predeterminado, activo, fecha_alta)
VALUES
  ('EXPEDICION', 'Expedicion', @suc_portal, 'EXPEDICION', 1, 1, NOW()),
  ('ENTRADA1',   'Entrada 1',   @suc_portal, 'ENTRADA',    0, 1, NOW()),
  ('ENTRADA2',   'Entrada 2',   @suc_portal, 'ENTRADA',    0, 1, NOW()),
  ('PERSONAL',   'Entrada Personal', @suc_portal, 'PERSONAL', 0, 1, NOW());


INSERT IGNORE INTO exp_portal_lector (id_portal, nombre, ip, modelo, canales, activo, fecha_alta)
SELECT id_portal, 'Lector principal', '192.168.3.60', 'E710 8 canales', 8, 1, NOW()
FROM exp_portal WHERE codigo = 'EXPEDICION';

INSERT IGNORE INTO exp_portal_lector (id_portal, nombre, ip, modelo, canales, activo, fecha_alta)
SELECT id_portal, 'Lector principal', '192.168.3.61', 'E710 8 canales', 8, 1, NOW()
FROM exp_portal WHERE codigo = 'ENTRADA1';

INSERT IGNORE INTO exp_portal_lector (id_portal, nombre, ip, modelo, canales, activo, fecha_alta)
SELECT id_portal, 'Lector principal', '192.168.3.62', 'E710 8 canales', 8, 1, NOW()
FROM exp_portal WHERE codigo = 'ENTRADA2';

INSERT IGNORE INTO exp_portal_lector (id_portal, nombre, ip, modelo, canales, activo, fecha_alta)
SELECT id_portal, 'Lector principal', '192.168.3.63', 'E710 8 canales', 8, 1, NOW()
FROM exp_portal WHERE codigo = 'PERSONAL';


-- 8 puertos por lector. 1-4 activos hoy; 5-8 se habilitan al conectar la segunda barra.
-- Se usa una tabla derivada (no temporal) para no requerir CREATE TEMPORARY TABLES.
INSERT IGNORE INTO exp_portal_antena (id_lector, puerto, activa, potencia, rssi_min)
SELECT l.id_lector,
       p.puerto,
       IF(p.puerto <= 4, 1, 0),
       20,
       -80
FROM exp_portal_lector l
CROSS JOIN (
  SELECT 1 AS puerto
  UNION ALL SELECT 2
  UNION ALL SELECT 3
  UNION ALL SELECT 4
  UNION ALL SELECT 5
  UNION ALL SELECT 6
  UNION ALL SELECT 7
  UNION ALL SELECT 8
) p;


-- Verificacion.
SELECT id_portal, codigo, nombre, suc, tipo, predeterminado, activo
FROM exp_portal
ORDER BY id_portal;

SELECT p.codigo, l.id_lector, l.nombre, l.ip, l.canales, l.activo
FROM exp_portal_lector l
INNER JOIN exp_portal p ON p.id_portal = l.id_portal
ORDER BY p.id_portal;

SELECT p.codigo, a.puerto, a.activa, a.potencia, a.rssi_min
FROM exp_portal_antena a
INNER JOIN exp_portal_lector l ON l.id_lector = a.id_lector
INNER JOIN exp_portal p ON p.id_portal = l.id_portal
ORDER BY p.id_portal, a.puerto;


-- ROLLBACK DE DATOS INICIALES (NO ejecutar sin confirmacion):
-- DELETE FROM exp_portal_antena
-- WHERE id_lector IN (SELECT id_lector FROM exp_portal_lector
--   WHERE ip IN ('192.168.3.60','192.168.3.61','192.168.3.62','192.168.3.63'));
-- DELETE FROM exp_portal_lector
-- WHERE ip IN ('192.168.3.60','192.168.3.61','192.168.3.62','192.168.3.63');
-- DELETE FROM exp_portal
-- WHERE codigo IN ('EXPEDICION','ENTRADA1','ENTRADA2','PERSONAL');
