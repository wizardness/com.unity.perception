using System;

namespace UnityEditor.Perception.GroundTruth.DatasetConsumer
{
    public class AddConsumerEndpointMenuAttribute : Attribute
    {
        public string menuPath;

        public AddConsumerEndpointMenuAttribute(string menuPath)
        {
            this.menuPath = menuPath;
        }
    }
}
