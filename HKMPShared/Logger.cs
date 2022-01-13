using System;

namespace Hkmp {
    public static class Logger {
        private static ILogger _logger;
        public static ILogger Log { get => _logger; set => _logger = value; }
        [Obsolete("Use the .Log property instead.")]
        public static ILogger Get() =>
            Log;
        [Obsolete("Use the .Log property instead.")]
        public static ILogger SetLogger(ILogger logger) =>
            Log = logger;
    }
}