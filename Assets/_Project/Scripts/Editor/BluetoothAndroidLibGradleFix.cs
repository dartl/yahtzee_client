using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Yahtzee.Editor
{
    /// <summary>
    /// AGP 8+ требует namespace у library-модулей. Unity генерирует
    /// Bluetooth.androidlib/build.gradle без него — дописываем после экспорта.
    /// </summary>
    public sealed class BluetoothAndroidLibGradleFix : IPostGenerateGradleAndroidProject
    {
        private const string NamespaceLine = "    namespace \"com.yahtzee.bluetooth\"";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var gradlePath = ResolveBluetoothGradle(path);
            if (string.IsNullOrEmpty(gradlePath))
            {
                Debug.LogWarning("Yahtzee Android: Bluetooth.androidlib/build.gradle не найден рядом с " + path);
                return;
            }

            var text = File.ReadAllText(gradlePath);
            if (text.Contains("namespace "))
                return;

            const string marker = "android {";
            var index = text.IndexOf(marker);
            if (index < 0)
            {
                Debug.LogError("Yahtzee Android: в Bluetooth.androidlib/build.gradle нет блока android {");
                return;
            }

            text = text.Insert(index + marker.Length, "\n" + NamespaceLine);
            File.WriteAllText(gradlePath, text);
            Debug.Log("Yahtzee Android: добавлен namespace → " + gradlePath);
        }

        private static string ResolveBluetoothGradle(string path)
        {
            var direct = Path.Combine(path, "Bluetooth.androidlib", "build.gradle");
            if (File.Exists(direct))
                return direct;

            var nested = Path.Combine(path, "unityLibrary", "Bluetooth.androidlib", "build.gradle");
            return File.Exists(nested) ? nested : null;
        }
    }
}
