using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;

namespace UnityEngine.Perception.Settings
{
    public class PerceptionSettings : ScriptableObject
    {
        const string k_SettingsPath = "Assets/Settings/PerceptionSettings.asset";

        [SerializeField]
        ConsumerEndpoint endpoint;

        public static ConsumerEndpoint Endpoint => GetOrCreateSettings().endpoint;

        static PerceptionSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PerceptionSettings>(k_SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PerceptionSettings>();
                settings.endpoint = null;
                AssetDatabase.CreateAsset(settings, k_SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        public static bool IsSettingsAvailable()
        {
            return File.Exists(PerceptionSettings.k_SettingsPath);
        }

        public static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }
    }
}
