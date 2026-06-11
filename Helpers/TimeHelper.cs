namespace QuinielaApp.Helpers
{
    /// <summary>
    /// Todos los usuarios son de El Salvador (UTC-6, sin horario de verano).
    /// Internamente todo se guarda en UTC.
    /// Este helper convierte entre UTC y hora local de El Salvador para mostrar en la UI.
    /// El Salvador no observa DST, así que el offset es siempre fijo: -6h.
    /// </summary>
    public static class TimeHelper
    {
        private static readonly TimeSpan SvOffset = TimeSpan.FromHours(-6);

        /// <summary>Convierte UTC a hora de El Salvador para mostrar en pantalla.</summary>
        public static DateTime ToSvTime(DateTime utc)
        {
            var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(u + SvOffset, DateTimeKind.Unspecified);
        }

        /// <summary>Convierte hora local de El Salvador a UTC para guardar en BD.</summary>
        public static DateTime ToUtc(DateTime svLocal)
        {
            var l = DateTime.SpecifyKind(svLocal, DateTimeKind.Unspecified);
            return DateTime.SpecifyKind(l - SvOffset, DateTimeKind.Utc);
        }

        /// <summary>Hora actual en El Salvador.</summary>
        public static DateTime NowSv() => ToSvTime(DateTime.UtcNow);

        /// <summary>Formatea fecha en hora de El Salvador con formato legible.</summary>
        public static string Format(DateTime utc, string format = "dd/MM/yyyy HH:mm") =>
            ToSvTime(utc).ToString(format);

        /// <summary>Formatea fecha con etiqueta de zona horaria.</summary>
        public static string FormatWithZone(DateTime utc, string format = "dd MMM, HH:mm") =>
            ToSvTime(utc).ToString(format) + " (hora SV)";
    }
}
