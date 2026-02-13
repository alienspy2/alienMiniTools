# Phase 6: 물리 엔진 통합 (선택사항)

## 목표
3D 및 2D 물리 시뮬레이션을 통합하여 Unity의 물리 기능을 재현합니다.

> **참고:** 물리 엔진은 모든 게임에 필수는 아닙니다.
> 물리가 필요한 게임을 만들 때 이 Phase를 진행하세요.

---

## 작업 항목

### 6.1 3D 물리: BepuPhysics v2 통합

**PhysicsManager3D.cs (IronRose.Physics):**
```csharp
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Memory;
using System.Numerics;

namespace IronRose.Physics
{
    public class PhysicsManager3D
    {
        private Simulation _simulation = null!;
        private BufferPool _bufferPool = null!;

        public void Initialize()
        {
            _bufferPool = new BufferPool();

            var targetThreadCount = Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 2 : 2;
            var threadDispatcher = new ThreadDispatcher(targetThreadCount);

            _simulation = Simulation.Create(
                _bufferPool,
                new NarrowPhaseCallbacks(),
                new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), // 중력
                new SolveDescription(8, 1)
            );

            Console.WriteLine($"[Physics3D] Initialized with {targetThreadCount} threads");
        }

        public void FixedUpdate(float deltaTime)
        {
            _simulation.Timestep(deltaTime);
        }

        public BodyHandle CreateDynamicBox(Vector3 position, Vector3 size, float mass)
        {
            var box = new Box(size.X, size.Y, size.Z);
            var shape = _simulation.Shapes.Add(box);

            var bodyDescription = BodyDescription.CreateDynamic(
                new RigidPose(position),
                new BodyInertia { InverseMass = 1f / mass },
                shape,
                0.01f
            );

            return _simulation.Bodies.Add(bodyDescription);
        }

        public void Dispose()
        {
            _simulation?.Dispose();
            _bufferPool?.Clear();
        }
    }

    internal struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            return true;
        }
        // ... 다른 필수 메서드 구현 ...
    }

    internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private Vector3 _gravity;

        public PoseIntegratorCallbacks(Vector3 gravity)
        {
            _gravity = gravity;
        }

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt) { }

        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
            ref BodyVelocityWide velocity)
        {
            velocity.Linear.Y += _gravity.Y * dt;
        }
    }
}
```

**Rigidbody.cs (Unity 스타일 래퍼):**
```csharp
using BepuPhysics;
using IronRose.Physics;

namespace UnityEngine
{
    public class Rigidbody : Component
    {
        private BodyHandle _bepuBody;
        internal PhysicsManager3D? physicsManager { get; set; }

        public Vector3 velocity
        {
            get
            {
                // Bepu에서 속도 가져오기
                return Vector3.zero; // TODO: 구현
            }
            set
            {
                // Bepu에 속도 설정
            }
        }

        public float mass { get; set; } = 1.0f;
        public bool useGravity { get; set; } = true;

        public void AddForce(Vector3 force)
        {
            // Bepu에 힘 적용
        }

        public void AddTorque(Vector3 torque)
        {
            // Bepu에 토크 적용
        }

        internal void SyncFromPhysics()
        {
            // Bepu의 위치/회전을 Transform에 동기화
        }
    }
}
```

**BoxCollider.cs:**
```csharp
using BepuPhysics.Collidables;

namespace UnityEngine
{
    public class BoxCollider : Component
    {
        public Vector3 size = Vector3.one;
        internal CollidableHandle colliderHandle;

        public void CreateCollider()
        {
            // Bepu에 Box Collider 생성
        }
    }
}
```

---

### 6.2 2D 물리: Box2D 통합

**PhysicsManager2D.cs:**
```csharp
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.Bodies;
using System.Numerics;

namespace IronRose.Physics
{
    public class PhysicsManager2D
    {
        private World _world = null!;

        public void Initialize()
        {
            _world = new World(new Vector2(0, -9.81f)); // 중력
            Console.WriteLine("[Physics2D] Initialized");
        }

        public void FixedUpdate(float deltaTime)
        {
            _world.Step(deltaTime, velocityIterations: 8, positionIterations: 3);
        }

        public Body CreateDynamicBox(Vector2 position, Vector2 size, float density)
        {
            var bodyDef = new BodyDef
            {
                type = BodyType.Dynamic,
                position = position
            };

            var body = _world.CreateBody(bodyDef);

            var shape = new Box2D.NetStandard.Collision.Shapes.PolygonShape();
            shape.SetAsBox(size.X / 2f, size.Y / 2f);

            body.CreateFixture(shape, density);

            return body;
        }
    }
}
```

**Rigidbody2D.cs:**
```csharp
using Box2D.NetStandard.Dynamics.Bodies;

namespace UnityEngine
{
    public class Rigidbody2D : Component
    {
        internal Body? box2dBody { get; set; }

        public Vector2 velocity
        {
            get
            {
                var v = box2dBody?.GetLinearVelocity() ?? System.Numerics.Vector2.Zero;
                return new Vector2(v.X, v.Y);
            }
            set
            {
                box2dBody?.SetLinearVelocity(new System.Numerics.Vector2(value.x, value.y));
            }
        }

        public float mass { get; set; } = 1.0f;
        public float gravityScale { get; set; } = 1.0f;

        public void AddForce(Vector2 force)
        {
            box2dBody?.ApplyForceToCenter(new System.Numerics.Vector2(force.x, force.y), true);
        }
    }
}
```

---

### 6.3 물리 시뮬레이션 루프

**PhysicsLoop.cs (IronRose.Bootstrapper):**
```csharp
using IronRose.Physics;
using System.Diagnostics;

namespace IronRose.Bootstrapper
{
    public class PhysicsLoop
    {
        private PhysicsManager3D _physics3D = new();
        private PhysicsManager2D _physics2D = new();

        private const float FixedDeltaTime = 1f / 50f; // 50 FPS
        private double _accumulator = 0;

        public void Initialize()
        {
            _physics3D.Initialize();
            _physics2D.Initialize();
        }

        public void Update(float deltaTime)
        {
            _accumulator += deltaTime;

            // Fixed timestep
            while (_accumulator >= FixedDeltaTime)
            {
                _physics3D.FixedUpdate(FixedDeltaTime);
                _physics2D.FixedUpdate(FixedDeltaTime);

                SyncPhysicsToTransforms();

                _accumulator -= FixedDeltaTime;
            }
        }

        private void SyncPhysicsToTransforms()
        {
            // 모든 Rigidbody의 위치를 Transform에 동기화
        }
    }
}
```

---

### 6.4 Unity 물리 API 호환

**Physics.cs:**
```csharp
namespace UnityEngine
{
    public struct RaycastHit
    {
        public GameObject gameObject;
        public float distance;
        public Vector3 point;
        public Vector3 normal;
    }

    public static class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = Mathf.Infinity)
        {
            // BepuPhysics의 Raycast 사용
            hit = default;
            // TODO: 구현
            return false;
        }

        public static Collider[] OverlapSphere(Vector3 position, float radius)
        {
            // BepuPhysics의 Overlap 쿼리 사용
            return Array.Empty<Collider>();
        }
    }
}
```

**Physics2D.cs:**
```csharp
namespace UnityEngine
{
    public struct RaycastHit2D
    {
        public GameObject gameObject;
        public float distance;
        public Vector2 point;
        public Vector2 normal;
    }

    public static class Physics2D
    {
        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance = Mathf.Infinity)
        {
            // Box2D의 Raycast 사용
            return default;
        }

        public static Collider2D[] OverlapCircle(Vector2 point, float radius)
        {
            // Box2D의 Overlap 쿼리 사용
            return Array.Empty<Collider2D>();
        }
    }
}
```

---

## 검증 기준

✅ 큐브가 바닥으로 떨어지는 중력 시뮬레이션:
```csharp
public class FallingCube : MonoBehaviour
{
    void Start()
    {
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 1.0f;
        rb.useGravity = true;

        var collider = gameObject.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
    }

    void Update()
    {
        Debug.Log($"Position: {transform.position}");
    }
}
```

✅ 두 오브젝트가 충돌하면 OnCollisionEnter 콜백 호출
✅ Raycast로 마우스 클릭한 오브젝트 감지

---

## 참고 자료
- [BepuPhysics v2 Documentation](https://github.com/bepu/bepuphysics2)
- [Box2D Manual](https://box2d.org/documentation/)

---

## 예상 소요 시간
**4-6일** (선택사항이므로 필요시에만)

---

## 다음 단계
→ [Phase 7: Deferred Rendering & PBR](Phase7_DeferredPBR.md)
