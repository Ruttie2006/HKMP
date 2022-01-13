namespace Hkmp {
    public static class Logger {
        private static ILogger _logger;
        public static ILogger Log { get => _logger; set => _logger = value; }
    }
}