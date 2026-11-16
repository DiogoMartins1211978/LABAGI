using UnityEditor;
using UnityEngine;

namespace Tools.Android.Editor
{
    internal static class AndroidLogs
    {
        [MenuItem("Tools/Android Logs")]
        static void Open()
        {
            // Reuse Unity's device selection, filters and log export controls.
            if (!EditorApplication.ExecuteMenuItem("Window/Analysis/Android Logcat"))
                Debug.LogWarning("Android Logcat is unavailable. Let Package Manager finish importing the project's packages, then try again.");
        }
    }
}
