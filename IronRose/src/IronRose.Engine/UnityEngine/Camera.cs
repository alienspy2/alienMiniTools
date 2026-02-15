namespace UnityEngine
{
    public enum CameraClearFlags
    {
        Skybox = 1,
        SolidColor = 2,
    }

    public class Camera : Component
    {
        public float fieldOfView = 60f;
        public float nearClipPlane = 0.1f;
        public float farClipPlane = 1000f;

        public CameraClearFlags clearFlags = CameraClearFlags.Skybox;
        public Color backgroundColor = new Color(0.192f, 0.302f, 0.475f, 1f);

        public static Camera? main { get; internal set; }

        internal override void OnAddedToGameObject()
        {
            if (main == null)
                main = this;
        }

        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4x4.LookAt(
                transform.position,
                transform.position + transform.forward,
                transform.up);
        }

        public Matrix4x4 GetProjectionMatrix(float aspectRatio)
        {
            return Matrix4x4.Perspective(fieldOfView, aspectRatio, nearClipPlane, farClipPlane);
        }

        internal static void ClearMain()
        {
            main = null;
        }
    }
}
