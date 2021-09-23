using System;
using System.Net;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.UIElements;

namespace UnityEditor.Perception.GroundTruth.DatasetConsumer
{
    public class EndpointList : VisualElement
    {
        VisualElement m_Container;
        SerializedProperty m_Property;

        public EndpointList(SerializedProperty property)
        {
            m_Property = property;
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{StaticData.uxmlDir}/EndpointList.uxml").CloneTree(this);

            m_Container = this.Q<VisualElement>("consumer-endpoint-container");

            var addEndpointButton = this.Q<Button>("add-consumer-endpoint-button");
            addEndpointButton.clicked += () =>
            {
                inspectorContainer.Add(new AddConsumerEndpointMenu(inspectorContainer, addEndpointButton, this));
            };
#if false
            var expandAllButton = this.Q<Button>("expand-all");
            expandAllButton.clicked += () => CollapseEndpoints(false);

            var collapseAllButton = this.Q<Button>("collapse-all");
            collapseAllButton.clicked += () => CollapseEndpoints(true);
#endif
            RefreshList();
            Undo.undoRedoPerformed += () =>
            {
                m_Property.serializedObject.Update();
                RefreshList();
            };
        }
#if false
        public DatasetCapture datasetCapture => (DatasetCapture)m_Property.serializedObject.targetObject;
#endif
        VisualElement inspectorContainer
        {
            get
            {
                var viewport = parent;
                while (!viewport.ClassListContains("unity-inspector-main-container"))
                    viewport = viewport.parent;
                return viewport;
            }
        }

        void RefreshList()
        {
            m_Container.Clear();
            if (m_Property.arraySize > 0 &&
                string.IsNullOrEmpty(m_Property.GetArrayElementAtIndex(0).managedReferenceFullTypename))
            {
                var textElement = new TextElement()
                {
                    text = "One or more endpoints have missing scripts. See console for more info."
                };
                textElement.AddToClassList("dataset_capture__info-box");
                textElement.AddToClassList("dataset_capture__error-box");
                m_Container.Add(textElement);
                return;
            }

            for (var i = 0; i < m_Property.arraySize; i++)
                m_Container.Add(new ConsumerEndpointElement(m_Property.GetArrayElementAtIndex(i), this));
        }
#if false
        public void AddEndpoint(Type endpointType)
        {
            Undo.RegisterCompleteObjectUndo(m_Property.serializedObject.targetObject, "Add Consumer Endpoint");
            datasetCapture.CreateConsumerEndpoint(endpointType);
            m_Property.serializedObject.Update();
            RefreshList();
        }

        public void RemoveEndpoint(ConsumerEndpointElement element)
        {
            Undo.RegisterCompleteObjectUndo(m_Property.serializedObject.targetObject, "Remove Consumer Endpoint");
            datasetCapture.RemoveConsumerEndpointAt(element.parent.IndexOf(element));
            m_Property.serializedObject.Update();
            RefreshList();
        }

        public void ReorderEndpoints(int currentIndex, int nextIndex)
        {
            if (currentIndex == nextIndex)
                return;
            if (nextIndex > currentIndex)
                nextIndex--;
            Undo.RegisterCompleteObjectUndo(m_Property.serializedObject.targetObject, "Reorder Consumer Endpoint");
            var endpoint = datasetCapture.GetConsumerEndpoint(currentIndex);
            datasetCapture.RemoveConsumerEndpointAt(currentIndex);
            datasetCapture.InsertConsumerEndpoint(nextIndex, endpoint);
            m_Property.serializedObject.Update();
            RefreshList();
        }

        void CollapseEndpoints(bool collapsed)
        {
            foreach (var child in m_Container.Children())
                ((ConsumerEndpointElement)child).collapsed = collapsed;
        }
#endif
    }
}
