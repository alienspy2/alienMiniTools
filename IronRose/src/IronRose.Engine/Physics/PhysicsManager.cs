using IronRose.Physics;
using RoseEngine;

namespace IronRose.Engine
{
    public class PhysicsManager : IDisposable
    {
        private PhysicsWorld3D _world3D = new();
        private PhysicsWorld2D _world2D = new();

        internal PhysicsWorld3D World3D => _world3D;
        internal PhysicsWorld2D World2D => _world2D;

        internal static PhysicsManager? Instance { get; private set; }

        public void Initialize()
        {
            _world3D.Initialize();
            _world2D.Initialize();
            Instance = this;
        }

        /// <summary>씬 전환 시 물리 월드 초기화 (모든 body 제거)</summary>
        public void Reset()
        {
            _world3D.Reset();
            _world2D.Reset();
        }

        public void FixedUpdate(float fixedDeltaTime)
        {
            // 1. Kinematic: Transform → Physics 동기화
            SyncTransformsToPhysics();

            // 2. 물리 시뮬레이션 스텝
            _world3D.Step(fixedDeltaTime);
            _world2D.Step(fixedDeltaTime);

            // 3. Dynamic: Physics → Transform 동기화
            SyncPhysicsToTransforms();
        }

        private void SyncTransformsToPhysics()
        {
            foreach (var rb in Rigidbody._allRigidbodies)
            {
                if (rb._isDestroyed || !rb.gameObject.activeInHierarchy) continue;
                if (rb.isKinematic)
                    rb.SyncToPhysics();
            }

            foreach (var rb2d in Rigidbody2D._allRigidbodies2D)
            {
                if (rb2d._isDestroyed || !rb2d.gameObject.activeInHierarchy) continue;
                if (rb2d.bodyType == RigidbodyType2D.Kinematic)
                    rb2d.SyncToPhysics();
            }
        }

        private void SyncPhysicsToTransforms()
        {
            foreach (var rb in Rigidbody._allRigidbodies)
            {
                if (rb._isDestroyed || !rb.gameObject.activeInHierarchy) continue;
                if (!rb.isKinematic)
                    rb.SyncFromPhysics();
            }

            foreach (var rb2d in Rigidbody2D._allRigidbodies2D)
            {
                if (rb2d._isDestroyed || !rb2d.gameObject.activeInHierarchy) continue;
                if (rb2d.bodyType == RigidbodyType2D.Dynamic)
                    rb2d.SyncFromPhysics();
            }
        }

        public void Dispose()
        {
            _world3D.Dispose();
            _world2D.Dispose();
            Instance = null;
        }
    }
}
