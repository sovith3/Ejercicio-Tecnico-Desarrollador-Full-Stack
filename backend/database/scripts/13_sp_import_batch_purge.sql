-- =============================================================================================
-- InvoiceHub - sp_ImportBatchPurge
-- ---------------------------------------------------------------------------------------------
-- Paso 4 de la carga masiva: limpia el lote de la tabla temporal una vez que la aplicacion ya leyo
-- los desenlaces. Es el complemento de sp_ImportBatchStage: la tabla es temporal (vive en la conexion
-- de la rutina), pero el token permite ademas reutilizar la conexion sin arrastrar filas de lotes
-- anteriores ni crecer en memoria durante cargas largas.
--
-- Parametros:
--   @BatchToken (TEXT) token del lote que se descarta.
-- =============================================================================================

DELETE FROM stg_invoice_import WHERE BatchToken = @BatchToken;
