using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Perception.Settings;
using UnityEngine.UIElements;

namespace UnityEditor.Perception.GroundTruth
{
    public class PerceptionSettingsProvider : SettingsProvider
    {
        private SerializedObject m_CustomSettings;
        public static string projectPath = "Project/Perception";

        class Styles
        {
            public static GUIContent endpoint = new GUIContent("Active Endpoint");
        }

        public PerceptionSettingsProvider(string path, SettingsScope scope = SettingsScope.User)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            m_CustomSettings = PerceptionSettings.GetSerializedSettings();
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.PropertyField(m_CustomSettings.FindProperty("endpoint"), Styles.endpoint);
            m_CustomSettings.ApplyModifiedProperties();
        }

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider()
        {
            if (PerceptionSettings.IsSettingsAvailable())
            {
                var provider = new PerceptionSettingsProvider(projectPath, SettingsScope.Project);

                // Automatically extract all keywords from the Styles.
                provider.keywords = GetSearchKeywordsFromGUIContentProperties<Styles>();
                return provider;
            }

            // Settings Asset doesn't exist yet; no need to display anything in the Settings window.
            return null;
        }
    }

    static class PerceptionSettingsRegister
    {
        [SettingsProvider]
        public static SettingsProvider CreatePerceptionSettingsProvider()
        {
            var provider = new SettingsProvider(PerceptionSettingsProvider.projectPath, SettingsScope.Project)
            {
                label = "Perception",

                activateHandler = (searchContext, rootElement) =>
                {
                    var settings = PerceptionSettings.GetSerializedSettings();

                    // rootElement is a VisualElement. If you add any children to it, the OnGUI function
                    // isn't called because the SettingsProvider uses the UIElements drawing framework.
                    var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.perception/Editor/GroundTruth/Uss/Styles.uss");
                    rootElement.styleSheets.Add(styleSheet);
                    var title = new Label()
                    {
                        text = "Perception"
                    };
                    title.AddToClassList("title");
                    rootElement.Add(title);

                    var properties = new VisualElement()
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Column
                        }
                    };
                    properties.AddToClassList("property-list");
                    rootElement.Add(properties);

                    var tf = new TextField()
                    {
                        value = settings.FindProperty("m_SomeString").stringValue
                    };
                    tf.AddToClassList("property-value");
                    properties.Add(tf);
                },

                keywords = new HashSet<string>(new[] { "Active Endpoint" })
            };

            return provider;
        }
    }
}

