using System.Collections.Generic;
using BepuPhysics;
using SysVector3 = System.Numerics.Vector3;
using SysQuaternion = System.Numerics.Quaternion;

namespace UnityEngine
{
    public class Rigidbody : Component
    {
        internal static readonly List<Rigidbody> _allRigidbodies = new();
        internal BodyHandle? bodyHandle;
        internal StaticHandle? staticHandle;

        public float mass { get; set; } = 1.0f;
        public float drag { get; set; } = 0f;
        public float angularDrag { get; set; } = 0.05f;
        public bool useGravity { get; set; } = true;
        public bool isKinematic { get; set; } = false;

        public Vector3 velocity
        {
            get
            {
                if (bodyHandle == null) return Vector3.zero;
                var mgr = IronRose.Engine.PhysicsManager.Instance;
                if (mgr == null) return Vector3.zero;
                var bv = mgr.World3D.GetBodyVelocity(bodyHandle.Value);
                return new Vector3(bv.Linear.X, bv.Linear.Y, bv.Linear.Z);
            }
            set
            {
                if (bodyHandle == null) return;
                var mgr = IronRose.Engine.PhysicsManager.Instance;
                if (mgr == null) return;
                var bv = mgr.World3D.GetBodyVelocity(bodyHandle.Value);
                bv.Linear = new SysVector3(value.x, value.y, value.z);
                mgr.World3D.SetBodyVelocity(bodyHandle.Value, bv);
            }
        }

        public Vector3 angularVelocity
        {
            get
            {
                if (bodyHandle == null) return Vector3.zero;
                var mgr = IronRose.Engine.PhysicsManager.Instance;
                if (mgr == null) return Vector3.zero;
                var bv = mgr.World3D.GetBodyVelocity(bodyHandle.Value);
                return new Vector3(bv.Angular.X, bv.Angular.Y, bv.Angular.Z);
            }
            set
            {
                if (bodyHandle == null) return;
                var mgr = IronRose.Engine.PhysicsManager.Instance;
                if (mgr == null) return;
                var bv = mgr.World3D.GetBodyVelocity(bodyHandle.Value);
                bv.Angular = new SysVector3(value.x, value.y, value.z);
                mgr.World3D.SetBodyVelocity(bodyHandle.Value, bv);
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (bodyHandle == null) return;
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            var f = new SysVector3(force.x, force.y, force.z);
            switch (mode)
            {
                case ForceMode.Force:
                    // 연속 힘: F * dt로 변환 (FixedUpdate 주기에 맞춰 누적)
                    mgr.World3D.ApplyLinearImpulse(bodyHandle.Value, f * Time.fixedDeltaTime);
                    break;
                case ForceMode.Impulse:
                    mgr.World3D.ApplyLinearImpulse(bodyHandle.Value, f);
                    break;
                case ForceMode.VelocityChange:
                    var bv = mgr.World3D.GetBodyVelocity(bodyHandle.Value);
                    bv.Linear += f;
                    mgr.World3D.SetBodyVelocity(bodyHandle.Value, bv);
                    break;
                case ForceMode.Acceleration:
                    mgr.World3D.ApplyLinearImpulse(bodyHandle.Value, f * mass * Time.fixedDeltaTime);
                    break;
            }
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (bodyHandle == null) return;
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            var t = new SysVector3(torque.x, torque.y, torque.z);
            if (mode == ForceMode.Impulse)
                mgr.World3D.ApplyAngularImpulse(bodyHandle.Value, t);
            else
                mgr.World3D.ApplyAngularImpulse(bodyHandle.Value, t * Time.fixedDeltaTime);
        }

        internal void SyncFromPhysics()
        {
            EnsureRegistered();
            if (bodyHandle == null) return;
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            var pose = mgr.World3D.GetBodyPose(bodyHandle.Value);
            transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            transform.rotation = new Quaternion(
                pose.Orientation.X, pose.Orientation.Y,
                pose.Orientation.Z, pose.Orientation.W);
        }

        internal void SyncToPhysics()
        {
            EnsureRegistered();
            if (bodyHandle == null) return;
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            var pos = transform.position;
            var rot = transform.rotation;
            mgr.World3D.SetBodyPose(bodyHandle.Value,
                new BepuPhysics.RigidPose(
                    new SysVector3(pos.x, pos.y, pos.z),
                    new SysQuaternion(rot.x, rot.y, rot.z, rot.w)));
        }

        private bool _registered;

        internal void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            RegisterWithPhysics();
        }

        internal override void OnAddedToGameObject()
        {
            _allRigidbodies.Add(this);
            // Deferred: 첫 FixedUpdate 시점에 등록 (isKinematic 등 프로퍼티 설정 이후)
        }

        private void RegisterWithPhysics()
        {
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            var pos = transform.position;
            var rot = transform.rotation;
            var sPos = new SysVector3(pos.x, pos.y, pos.z);
            var sRot = new SysQuaternion(rot.x, rot.y, rot.z, rot.w);

            // Collider 타입에 따라 shape 결정
            var boxCol = gameObject.GetComponent<BoxCollider>();
            var sphereCol = gameObject.GetComponent<SphereCollider>();
            var capsuleCol = gameObject.GetComponent<CapsuleCollider>();

            if (isKinematic)
            {
                if (boxCol != null)
                    bodyHandle = mgr.World3D.AddKinematicBox(sPos, sRot,
                        boxCol.size.x, boxCol.size.y, boxCol.size.z);
                else if (sphereCol != null)
                    bodyHandle = mgr.World3D.AddKinematicBox(sPos, sRot, 1, 1, 1); // fallback
                return;
            }

            // Dynamic body
            if (boxCol != null)
            {
                bodyHandle = mgr.World3D.AddDynamicBox(sPos, sRot,
                    boxCol.size.x, boxCol.size.y, boxCol.size.z, mass);
            }
            else if (sphereCol != null)
            {
                bodyHandle = mgr.World3D.AddDynamicSphere(sPos, sRot,
                    sphereCol.radius, mass);
            }
            else if (capsuleCol != null)
            {
                float capsuleLength = Mathf.Max(0.01f, capsuleCol.height - 2f * capsuleCol.radius);
                bodyHandle = mgr.World3D.AddDynamicCapsule(sPos, sRot,
                    capsuleCol.radius, capsuleLength, mass);
            }
            else
            {
                // Collider 없으면 기본 unit box
                bodyHandle = mgr.World3D.AddDynamicBox(sPos, sRot, 1f, 1f, 1f, mass);
            }
        }

        internal void RemoveFromPhysics()
        {
            var mgr = IronRose.Engine.PhysicsManager.Instance;
            if (mgr == null) return;

            if (bodyHandle != null)
            {
                mgr.World3D.RemoveBody(bodyHandle.Value);
                bodyHandle = null;
            }
            if (staticHandle != null)
            {
                mgr.World3D.RemoveStatic(staticHandle.Value);
                staticHandle = null;
            }
            _registered = false;
        }

        internal static void ClearAll() => _allRigidbodies.Clear();
    }
}
