using System.Collections.Generic;

namespace Hkmp.Util {
    /// <summary>
    /// Class for utilities on copying specific classes.
    /// </summary>
    internal static class CopyUtil {
        /// <summary>
        /// Make a copy of an array, which will preserve internal references in the objects.
        /// </summary>
        /// <param name="original">The original array.</param>
        /// <param name="objectDict">Dictionary containing references between objects in the original instance and
        /// the copied instance.</param>
        /// <typeparam name="T">The type of the objects in the array.</typeparam>
        /// <returns>A copied array.</returns>
        private static T[] SmartCopyArray<T>(T[] original, Dictionary<object, object> objectDict) {
            if (objectDict.ContainsKey(original)) {
                return (T[]) objectDict[original];
            }

            var newArray = new T[original.Length];
            for (var i = 0; i < original.Length; i++) {
                newArray[i] = original[i];
            }

            objectDict[original] = newArray;

            return newArray;
        }
    }
}
