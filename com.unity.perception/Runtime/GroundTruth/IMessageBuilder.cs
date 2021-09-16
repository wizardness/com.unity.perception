namespace UnityEngine.Perception.GroundTruth.Exporters.Solo
{
    public interface IMessageBuilder
    {
        void AddInt(string label, int value);
        void AddIntVector(string label, int[] values);

        void AddFloat(string label, float value);
        void AddFloatVector(string label, float[] value);

        void AddString(string label, string value);
        void AddStringVector(string label, object[] values);

        void AddBoolean(string label, bool value);
        void AddBooleanVector(string label, bool[] values);

        void AddPngImage(string label, byte[] value);

        IMessageBuilder AddNestedMessage(string label);
        IMessageBuilder AddNestedMessageToVector(string arrayLabel);
    }
}
