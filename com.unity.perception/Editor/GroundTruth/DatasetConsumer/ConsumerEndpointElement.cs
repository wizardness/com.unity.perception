using System;
using UnityEditor.Perception.Randomization;
using UnityEditor.UIElements;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.UIElements;

namespace UnityEditor.Perception.GroundTruth.DatasetConsumer
{
    public class ConsumerEndpointElement : VisualElement
    {
        const string k_CollapsedParameterClass = "collapsed";
        SerializedProperty m_Collapsed;
        VisualElement m_PropertiesContainer;
        SerializedProperty m_Property;

        public ConsumerEndpointElement(SerializedProperty property, EndpointList endpointList)
        {
            m_Property = property;
            this.endpointList = endpointList;

            m_Collapsed = property.FindPropertyRelative("collapsed");
            collapsed = m_Collapsed.boolValue;

            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{StaticData.uxmlDir}/ConsumerEndpoint/ConsumerEndpointElement.uxml").CloneTree(this);

            var classNameLabel = this.Q<TextElement>("class-name");
            var splitType = property.managedReferenceFullTypename.Split(' ', '.');
            classNameLabel.text = splitType[splitType.Length - 1];

            m_PropertiesContainer = this.Q<VisualElement>("properties");

            var collapseToggle = this.Q<VisualElement>("collapse");
            collapseToggle.RegisterCallback<MouseUpEvent>(evt => collapsed = !collapsed);

            var enabledToggle = this.Q<Toggle>("enabled");
            enabledToggle.BindProperty(property.FindPropertyRelative("m_Enabled"));

            var removeButton = this.Q<Button>("remove");
//            removeButton.clicked += () => endpointList.RemoveEndpoint(this);

//            this.AddManipulator(new DragToReorderManipulator());

            FillPropertiesContainer();
        }

        ConsumerEndpoint endpoint => (ConsumerEndpoint)StaticData.GetManagedReferenceValue(m_Property);

        public Type endpointType => endpoint.GetType();

        public EndpointList endpointList { get; }

        public bool collapsed
        {
            get => m_Collapsed?.boolValue ?? true;
            set
            {
                if (m_Collapsed == null)
                    return;

                m_Collapsed.boolValue = value;
                m_Property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                if (value)
                    AddToClassList(k_CollapsedParameterClass);
                else
                    RemoveFromClassList(k_CollapsedParameterClass);
            }
        }

        void FillPropertiesContainer()
        {
            m_PropertiesContainer.Clear();
            UIElementsEditorUtilities.CreatePropertyFields(m_Property, m_PropertiesContainer);

            if (m_PropertiesContainer.childCount == 0)
                m_PropertiesContainer.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
        }
    }
}
