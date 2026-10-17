-- =============================================================================================
-- InvoiceHub - sp_ImportBatchOutcomes
-- ---------------------------------------------------------------------------------------------
-- Paso 3 de la carga masiva: devuelve el desenlace de cada factura del lote tal como lo determino
-- sp_ImportBatchMerge, en el MISMO orden en que los registros llegaron en la carga (SourcePosition).
-- Ese orden es el contrato con la aplicacion: permite correlacionar posicionalmente los resultados
-- con los registros validados sin enviar de vuelta el contenido de la factura.
--
-- La posicion se devuelve a proposito: es la unica verificacion de completitud que no depende del
-- conteo de filas que informa el controlador (un lote de sentencias suma conteos de DDL y DML que no
-- representan al lote), sino del estado real que el motor tiene en la tabla temporal.
--
-- Columnas:
--   SourcePosition (INTEGER) posicion del registro dentro del lote (0..N-1).
--   Number         (INTEGER) numero de factura del registro.
--   Outcome        (INTEGER) ordinal de InvoiceImportResult: 0 Applied | 1 AlreadyExists.
--   InvoiceId      (INTEGER) identificador de la factura en el sistema (la creada o la ya existente).
--
-- Parametros:
--   @BatchToken (TEXT) token del lote en curso.
-- =============================================================================================

SELECT
    staged.SourcePosition AS SourcePosition,
    staged.Number         AS Number,
    staged.Outcome        AS Outcome,
    staged.InvoiceId      AS InvoiceId
FROM stg_invoice_import AS staged
WHERE staged.BatchToken = @BatchToken
ORDER BY staged.SourcePosition;
