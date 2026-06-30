-- migrate_knockout.sql
-- Ejecutar manualmente en la BD de QuinielaApp.
-- Agrega campos para funcionalidad de eliminatorias (quién clasifica).
-- Usar con la BD configurada en appsettings.json.

USE [QuinielaDB]
GO

-- 1. Status real de la API en Matches ("FT", "AET", "PEN")
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[Matches]') AND name = N'ApiStatus'
)
BEGIN
    ALTER TABLE [Matches]
    ADD [ApiStatus] nvarchar(10) NULL;
    PRINT 'Columna ApiStatus agregada a Matches.';
END
ELSE
    PRINT 'Columna ApiStatus ya existe en Matches.';
GO

-- 2. Quién clasificó al final en Matches ("Home" | "Away")
--    Solo relevante cuando ApiStatus == "AET" o "PEN"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[Matches]') AND name = N'Qualifier'
)
BEGIN
    ALTER TABLE [Matches]
    ADD [Qualifier] nvarchar(10) NULL;
    PRINT 'Columna Qualifier agregada a Matches.';
END
ELSE
    PRINT 'Columna Qualifier ya existe en Matches.';
GO

-- 3. Predicción del usuario de quién clasifica en Predictions ("Home" | "Away")
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[Predictions]') AND name = N'QualifierPred'
)
BEGIN
    ALTER TABLE [Predictions]
    ADD [QualifierPred] nvarchar(10) NULL;
    PRINT 'Columna QualifierPred agregada a Predictions.';
END
ELSE
    PRINT 'Columna QualifierPred ya existe en Predictions.';
GO

-- 4. Predicción de penaltis en Predictions (true = penaltis, false = ET, null = sin predicción)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[Predictions]') AND name = N'PenaltyPred'
)
BEGIN
    ALTER TABLE [Predictions]
    ADD [PenaltyPred] bit NULL;
    PRINT 'Columna PenaltyPred agregada a Predictions.';
END
ELSE
    PRINT 'Columna PenaltyPred ya existe en Predictions.';
GO

-- 5. Registrar en historial EF para evitar que intente aplicar la migración
IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625000001_AddKnockoutFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260625000001_AddKnockoutFields', N'8.0.0');
    PRINT 'Migración registrada en __EFMigrationsHistory.';
END
GO

-- Valores válidos:
--   Matches.ApiStatus    → NULL (sin dato / en curso), 'FT', 'AET', 'PEN', 'AWD', 'WO'
--   Matches.Qualifier    → NULL (no aplica o sin dato), 'Home', 'Away'
--   Predictions.QualifierPred → NULL (sin predicción), 'Home', 'Away'

-- ============================================================
-- 6. Timestamp del último cálculo de puntos por partido
--    Permite detectar partidos cuyo ApiStatus/Qualifier
--    cambió DESPUÉS del cálculo (PointsCalculatedAt < LastUpdated).
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[Matches]') AND name = N'PointsCalculatedAt'
)
BEGIN
    ALTER TABLE [Matches]
    ADD [PointsCalculatedAt] datetime2 NULL;
    PRINT 'Columna PointsCalculatedAt agregada a Matches.';
END
ELSE
    PRINT 'Columna PointsCalculatedAt ya existe en Matches.';
GO

-- ============================================================
-- Diagnóstico: predicciones con PointsEarned > 5 donde el
-- partido tiene ApiStatus = 'FT' (bonus NO debería aplicar).
-- Ejecutar ANTES y DESPUÉS del recálculo masivo para medir
-- el alcance real del bug.
-- ============================================================
SELECT
    m.ApiStatus,
    COUNT(*)                                              AS total_predicciones,
    SUM(CASE WHEN p.PointsEarned = 7 THEN 1 ELSE 0 END) AS con_7_pts,
    SUM(CASE WHEN p.PointsEarned = 8 THEN 1 ELSE 0 END) AS con_8_pts
FROM Predictions p
JOIN Matches     m ON m.Id = p.MatchId
WHERE p.PointsEarned > 5
GROUP BY m.ApiStatus
ORDER BY m.ApiStatus;
GO
