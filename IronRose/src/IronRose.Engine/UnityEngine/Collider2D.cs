namespace UnityEngine
{
    public abstract class Collider2D : Component
    {
        public bool isTrigger { get; set; }
        public Vector2 offset { get; set; } = Vector2.zero;
    }

    public enum RigidbodyType2D
    {
        Dynamic,
        Kinematic,
        Static
    }
}
