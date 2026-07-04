-- Backfill de ApiStatus para partidos FT históricos (previos a que existiera el campo).
-- No es estrictamente necesario: el fix en ScoreSyncBackgroundService.cs ya excluye
-- estos partidos del fallback por antigüedad (MatchDate < 24h). Este script es solo
-- higiene de datos para dejar ApiStatus consistente.
--
-- Revisar el resultado del SELECT antes de correr el UPDATE.

SELECT COUNT(*) AS partidos_afectados
FROM Matches
WHERE Status = 'FT'
  AND ApiStatus IS NULL
  AND ApiMatchId NOT IN (9999001, 9999002, 9999003);

-- UPDATE Matches
-- SET ApiStatus = 'FT'
-- WHERE Status = 'FT'
--   AND ApiStatus IS NULL
--   AND ApiMatchId NOT IN (9999001, 9999002, 9999003);
