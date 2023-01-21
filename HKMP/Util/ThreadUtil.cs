using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hkmp.Util {
    internal class ThreadUtil {
        public static void RunActionOnMainThread(Action action) {
            action();
        }
    }
}
