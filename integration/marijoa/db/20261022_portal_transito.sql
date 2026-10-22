-- Apply MARIJOA_RULES.md strictly.
-- Portal RFID - EPCs temporales en transito.
-- Compatible con MariaDB 5.5 / MySQL 5.5.
--
-- Esta tabla no reemplaza exp_portal_fondo:
--   exp_portal_fondo   = etiquetas estaticas del entorno del portal.
--   exp_portal_transito = etiquetas documentadas que salieron y pueden
--                         permanecer temporalmente cerca del portal.
--
-- NO EJECUTAR sin confirmacion explicita de Marijoa.

SET NAMES 'latin1';

CREATE TABLE IF NOT EXISTS exp_portal_transito (
  id_transito BIGINT NOT NULL AUTO_INCREMENT,
  id_portal INT NOT NULL,
  id_sesion INT NULL DEFAULT NULL,
  tipo_documento VARCHAR(12) NOT NULL,
  id_documento BIGINT NOT NULL,
  epc VARCHAR(124) NOT NULL,
  lote VARCHAR(60) NULL DEFAULT NULL,
  fecha_salida DATETIME NOT NULL,
  fecha_limite DATETIME NOT NULL,
  activo TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (id_transito),
  UNIQUE KEY uq_exp_portal_transito_documento_epc
    (tipo_documento, id_documento, epc),
  KEY ref_exp_portal_transito_portal (id_portal, activo, fecha_limite),
  KEY ref_exp_portal_transito_epc (epc, activo)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;


-- Verificacion (solo lectura).
SELECT TABLE_NAME, TABLE_COLLATION
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'exp_portal_transito';


-- ROLLBACK (NO ejecutar sin las dos confirmaciones de MARIJOA_RULES.md):
-- DROP TABLE exp_portal_transito;
