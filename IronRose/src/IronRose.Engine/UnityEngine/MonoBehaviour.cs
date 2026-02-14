namespace UnityEngine
{
    public class MonoBehaviour : Component
    {
        public bool enabled { get; set; } = true;

        public virtual void Awake() { }
        public virtual void Start() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }
        public virtual void OnDestroy() { }
    }
}
