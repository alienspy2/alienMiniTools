using System.Collections.Generic;

namespace UnityEngine
{
    public abstract class Collider : Component
    {
        public bool isTrigger { get; set; }
        public Vector3 center { get; set; } = Vector3.zero;

        internal bool isRegistered;
    }
}
