namespace UnityEngine
{
    public class Transform : Component
    {
        public Vector3 position { get; set; } = Vector3.zero;
        public Quaternion rotation { get; set; } = Quaternion.identity;
        public Vector3 localScale { get; set; } = Vector3.one;

        public Vector3 eulerAngles
        {
            get => rotation.eulerAngles;
            set => rotation = Quaternion.Euler(value);
        }

        public Vector3 forward => rotation * Vector3.forward;
        public Vector3 right => rotation * Vector3.right;
        public Vector3 up => rotation * Vector3.up;

        public void Translate(Vector3 translation)
        {
            position += translation;
        }

        public void Translate(float x, float y, float z)
        {
            position += new Vector3(x, y, z);
        }

        public void Rotate(Vector3 eulers)
        {
            rotation = rotation * Quaternion.Euler(eulers);
        }

        public void Rotate(float xAngle, float yAngle, float zAngle)
        {
            rotation = rotation * Quaternion.Euler(xAngle, yAngle, zAngle);
        }
    }
}
