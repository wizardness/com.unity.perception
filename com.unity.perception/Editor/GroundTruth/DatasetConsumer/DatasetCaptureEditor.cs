using UnityEngine.Perception.GroundTruth;
using UnityEngine.UIElements;

namespace UnityEditor.Perception.GroundTruth.DatasetConsumer
{
    [CustomEditor(typeof(DatasetCapture), true)]
    public class DatasetCaptureEditor : Editor
    {
        DatasetCapture m_DatasetCapture;
        SerializedObject m_SerializedObject;
        VisualElement m_EndpointsPlaceholder;
        VisualElement m_Root;

        public override VisualElement CreateInspectorGUI()
        {
#if false
            m_DatasetCapture = (DatasetCapture)target;
            m_SerializedObject = new SerializedObject(m_DatasetCapture);
            m_Root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{StaticData.uxmlDir}/DatasetCaptureElement.uxml").CloneTree();

            m_EndpointsPlaceholder = m_Root.Q<VisualElement>("endpoint-list-placeholder");

//            m_EndpointsPlaceholder.Add(new EndpointList());

            return m_Root;
#endif
            return null;
        }
    }
}
