namespace QuinielaApp.Services
{
    public class PointsResult
    {
        public int  Points        { get; set; }
        public bool ResultCorrect { get; set; }
        public bool ScoreCorrect  { get; set; }
    }

    public static class PointsCalculator
    {
        /// <summary>
        /// Calcula puntos de una predicción según la especificación definitiva.
        /// Puro, sin dependencias, 100% determinístico.
        /// </summary>
        /// <param name="predHomeScore">Usar -1 si el usuario no predijo marcador.</param>
        /// <param name="predAwayScore">Usar -1 si el usuario no predijo marcador.</param>
        public static PointsResult Calculate(
            int     matchHomeScore,
            int     matchAwayScore,
            string? matchApiStatus,
            string? matchQualifier,
            bool    isKnockout,
            string? predResult,
            int     predHomeScore,
            int     predAwayScore,
            string? predQualifier,
            bool?   predPenalty)
        {
            // LOG TEMPORAL DE DEPURACIÓN — quitar después
            Console.WriteLine(
                $"[PTS-DEBUG] home={matchHomeScore} away={matchAwayScore} " +
                $"apiStatus='{matchApiStatus}' qualifier='{matchQualifier}' " +
                $"isKnockout={isKnockout} predResult='{predResult}' " +
                $"predHome={predHomeScore} predAway={predAwayScore} " +
                $"predQualifier='{predQualifier}' predPenalty={predPenalty}");

            // Paso A — resultado real al 90'
            string actual;
            if      (matchHomeScore > matchAwayScore) actual = "Home";
            else if (matchHomeScore < matchAwayScore) actual = "Away";
            else                                      actual = "Draw";

            // Paso B — aciertos base
            bool resultOk = string.Equals(
                predResult?.Trim(), actual, StringComparison.OrdinalIgnoreCase);
            bool scoreOk = predHomeScore == matchHomeScore &&
                           predAwayScore == matchAwayScore;

            // Paso C — puntos base (aplica siempre, grupos y eliminatorias)
            int pts;
            if      (scoreOk)  pts = 5;
            else if (resultOk) pts = 2;
            else               pts = 0;

            // Paso D — bonus eliminatorias (SOLO si knockout y basePts > 0)
            if (isKnockout && pts > 0)
            {
                bool huboExtension = matchApiStatus == "AET" || matchApiStatus == "PEN";

                if (huboExtension && predQualifier != null && matchQualifier != null)
                {
                    bool qualifierOk = string.Equals(
                        predQualifier.Trim(), matchQualifier.Trim(),
                        StringComparison.OrdinalIgnoreCase);

                    if (qualifierOk)
                    {
                        pts += 2;

                        if (matchApiStatus == "PEN" && predPenalty == true)
                            pts += 1;
                    }
                }
            }

            // LOG TEMPORAL DE DEPURACIÓN — quitar después
            Console.WriteLine(
                $"[PTS-DEBUG] → pts={pts} resultOk={resultOk} scoreOk={scoreOk}");

            // Paso E — resultado final
            return new PointsResult
            {
                Points        = pts,
                ResultCorrect = resultOk,
                ScoreCorrect  = scoreOk
            };
        }
    }
}
