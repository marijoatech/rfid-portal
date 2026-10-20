-- Apply MARIJOA_RULES.md strictly.
-- Portales RFID - SOLO estructura. Compatible con MySQL 5.5/5.7 y MariaDB 5.5.
-- NO EJECUTAR sin confirmacion explicita de Marijoa.
--
-- Este script no toca tablas existentes: crea tablas nuevas y un permiso nuevo.
-- Ejecutar sobre la base de Marijoa:
--   mysql --default-character-set=latin1 -u USUARIO -p BASE < 20261020_portal_rfid_tablas.sql

SET NAMES 'latin1';


CREATE TABLE IF NOT EXISTS exp_portal (
  id_portal INT NOT NULL AUTO_INCREMENT,
  codigo VARCHAR(20) NOT NULL,
  nombre VARCHAR(80) NOT NULL,
  suc VARCHAR(10) NOT NULL,
  tipo VARCHAR(20) NOT NULL DEFAULT 'EXPEDICION',
  predeterminado TINYINT(1) NOT NULL DEFAULT 0,
  activo TINYINT(1) NOT NULL DEFAULT 1,
  agente VARCHAR(60) NULL DEFAULT NULL,
  ultimo_heartbeat DATETIME NULL DEFAULT NULL,
  fecha_alta DATETIME NOT NULL,
  observacion VARCHAR(255) NULL DEFAULT NULL,
  PRIMARY KEY (id_portal),
  UNIQUE KEY uq_exp_portal_codigo (codigo),
  KEY ref_exp_portal_suc (suc),
  KEY ref_exp_portal_activo (activo)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


CREATE TABLE IF NOT EXISTS exp_portal_lector (
  id_lector INT NOT NULL AUTO_INCREMENT,
  id_portal INT NOT NULL,
  nombre VARCHAR(60) NOT NULL,
  ip VARCHAR(45) NOT NULL,
  modelo VARCHAR(40) NULL DEFAULT NULL,
  canales TINYINT NOT NULL DEFAULT 8,
  activo TINYINT(1) NOT NULL DEFAULT 1,
  ultimo_heartbeat DATETIME NULL DEFAULT NULL,
  estado VARCHAR(20) NULL DEFAULT NULL,
  estado_json MEDIUMTEXT NULL,
  fecha_alta DATETIME NOT NULL,
  PRIMARY KEY (id_lector),
  UNIQUE KEY uq_exp_portal_lector_ip (ip),
  KEY ref_exp_portal_lector_portal (id_portal),
  KEY ref_exp_portal_lector_activo (activo)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


CREATE TABLE IF NOT EXISTS exp_portal_antena (
  id_lector INT NOT NULL,
  puerto TINYINT NOT NULL,
  activa TINYINT(1) NOT NULL DEFAULT 1,
  potencia TINYINT NOT NULL DEFAULT 20,
  rssi_min SMALLINT NOT NULL DEFAULT -80,
  PRIMARY KEY (id_lector, puerto)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


CREATE TABLE IF NOT EXISTS exp_portal_fondo (
  id_fondo BIGINT NOT NULL AUTO_INCREMENT,
  id_lector INT NOT NULL,
  epc VARCHAR(124) NOT NULL,
  lote VARCHAR(60) NULL DEFAULT NULL,
  fecha DATETIME NOT NULL,
  activo TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (id_fondo),
  UNIQUE KEY uq_exp_portal_fondo_lector_epc (id_lector, epc),
  KEY ref_exp_portal_fondo_lector (id_lector)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


CREATE TABLE IF NOT EXISTS exp_portal_sesion (
  id_portal INT NOT NULL,
  id_sesion INT NOT NULL,
  estado VARCHAR(12) NOT NULL DEFAULT 'Armada',
  usuario VARCHAR(30) NOT NULL,
  fecha_ini DATETIME NOT NULL,
  fecha_fin DATETIME NULL DEFAULT NULL,
  PRIMARY KEY (id_portal, id_sesion),
  KEY ref_exp_portal_sesion_sesion (id_sesion),
  KEY ref_exp_portal_sesion_estado (id_portal, estado)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


CREATE TABLE IF NOT EXISTS exp_portal_comando (
  id_comando BIGINT NOT NULL AUTO_INCREMENT,
  id_portal INT NOT NULL,
  tipo VARCHAR(24) NOT NULL,
  payload MEDIUMTEXT NULL,
  estado VARCHAR(12) NOT NULL DEFAULT 'Pendiente',
  fecha_creado DATETIME NOT NULL,
  fecha_entregado DATETIME NULL DEFAULT NULL,
  fecha_ejecutado DATETIME NULL DEFAULT NULL,
  resultado VARCHAR(255) NULL DEFAULT NULL,
  PRIMARY KEY (id_comando),
  KEY ref_exp_portal_comando_pendiente (id_portal, estado)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


INSERT IGNORE INTO permisos (id_permiso, descripcion)
VALUES ('8.8', 'Acceso y administracion de Portales RFID');


-- Verificacion (solo lectura).
SELECT TABLE_NAME, TABLE_COLLATION
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN (
    'exp_portal',
    'exp_portal_lector',
    'exp_portal_antena',
    'exp_portal_fondo',
    'exp_portal_sesion',
    'exp_portal_comando'
  )
ORDER BY TABLE_NAME;

SELECT id_permiso, descripcion FROM permisos WHERE id_permiso = '8.8';


-- ROLLBACK (NO ejecutar sin las dos confirmaciones de MARIJOA_RULES.md):
-- DROP TABLE exp_portal_comando;
-- DROP TABLE exp_portal_sesion;
-- DROP TABLE exp_portal_fondo;
-- DROP TABLE exp_portal_antena;
-- DROP TABLE exp_portal_lector;
-- DROP TABLE exp_portal;
-- DELETE FROM permisos WHERE id_permiso = '8.8';
