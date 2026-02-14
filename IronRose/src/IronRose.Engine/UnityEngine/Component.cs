namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject { get; internal set; } = null!;
        public Transform transform => gameObject.transform;

        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
}
