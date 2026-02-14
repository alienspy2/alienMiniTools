using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace IronRose.Physics
{
    public class PhysicsWorld3D : IDisposable
    {
        private Simulation _simulation = null!;
        private BufferPool _bufferPool = null!;
        private ThreadDispatcher _threadDispatcher = null!;
        private Vector3 _gravity;

        public void Initialize(Vector3? gravity = null)
        {
            var g = gravity ?? new Vector3(0, -9.81f, 0);
            _gravity = g;

            _bufferPool = new BufferPool();
            var threadCount = Math.Max(2, Environment.ProcessorCount - 2);
            _threadDispatcher = new ThreadDispatcher(threadCount, 16384);

            _simulation = Simulation.Create(
                _bufferPool,
                new NarrowPhaseCallbacks(),
                new PoseIntegratorCallbacks(g),
                new SolveDescription(8, 1)
            );

            Console.WriteLine($"[Physics3D] Initialized with {threadCount} threads");
        }

        public void Step(float deltaTime)
        {
            _simulation.Timestep(deltaTime, _threadDispatcher);
        }

        // --- Dynamic Body (shape 생성 + body 등록을 한번에) ---

        public BodyHandle AddDynamicBox(Vector3 position, Quaternion rotation,
            float width, float height, float length, float mass)
        {
            var shape = new Box(width, height, length);
            return _simulation.Bodies.Add(
                BodyDescription.CreateConvexDynamic(
                    new RigidPose(position, rotation), mass, _simulation.Shapes, shape)
            );
        }

        public BodyHandle AddDynamicSphere(Vector3 position, Quaternion rotation,
            float radius, float mass)
        {
            var shape = new Sphere(radius);
            return _simulation.Bodies.Add(
                BodyDescription.CreateConvexDynamic(
                    new RigidPose(position, rotation), mass, _simulation.Shapes, shape)
            );
        }

        public BodyHandle AddDynamicCapsule(Vector3 position, Quaternion rotation,
            float radius, float length, float mass)
        {
            var shape = new Capsule(radius, length);
            return _simulation.Bodies.Add(
                BodyDescription.CreateConvexDynamic(
                    new RigidPose(position, rotation), mass, _simulation.Shapes, shape)
            );
        }

        // --- Static Body ---

        public StaticHandle AddStaticBox(Vector3 position, Quaternion rotation,
            float width, float height, float length)
        {
            var shape = new Box(width, height, length);
            var shapeIndex = _simulation.Shapes.Add(shape);
            return _simulation.Statics.Add(
                new StaticDescription(position, rotation, shapeIndex)
            );
        }

        public StaticHandle AddStaticSphere(Vector3 position, Quaternion rotation,
            float radius)
        {
            var shape = new Sphere(radius);
            var shapeIndex = _simulation.Shapes.Add(shape);
            return _simulation.Statics.Add(
                new StaticDescription(position, rotation, shapeIndex)
            );
        }

        // --- Kinematic Body ---

        public BodyHandle AddKinematicBox(Vector3 position, Quaternion rotation,
            float width, float height, float length)
        {
            var shape = new Box(width, height, length);
            return _simulation.Bodies.Add(
                BodyDescription.CreateConvexKinematic(
                    new RigidPose(position, rotation), _simulation.Shapes, shape)
            );
        }

        // --- Body 조회/수정 ---

        public RigidPose GetBodyPose(BodyHandle handle)
        {
            return _simulation.Bodies[handle].Pose;
        }

        public BodyVelocity GetBodyVelocity(BodyHandle handle)
        {
            return _simulation.Bodies[handle].Velocity;
        }

        public void SetBodyVelocity(BodyHandle handle, BodyVelocity velocity)
        {
            _simulation.Bodies[handle].Velocity = velocity;
        }

        public void ApplyLinearImpulse(BodyHandle handle, Vector3 impulse)
        {
            _simulation.Bodies[handle].ApplyLinearImpulse(impulse);
        }

        public void ApplyAngularImpulse(BodyHandle handle, Vector3 impulse)
        {
            _simulation.Bodies[handle].ApplyAngularImpulse(impulse);
        }

        public void SetBodyPose(BodyHandle handle, RigidPose pose)
        {
            _simulation.Bodies[handle].Pose = pose;
        }

        public void RemoveBody(BodyHandle handle)
        {
            _simulation.Bodies.Remove(handle);
        }

        public void RemoveStatic(StaticHandle handle)
        {
            _simulation.Statics.Remove(handle);
        }

        /// <summary>시뮬레이션 내 모든 body/static 제거 (BufferPool, ThreadDispatcher 유지)</summary>
        public void Reset()
        {
            _simulation?.Dispose();
            _simulation = Simulation.Create(
                _bufferPool,
                new NarrowPhaseCallbacks(),
                new PoseIntegratorCallbacks(_gravity),
                new SolveDescription(8, 1)
            );
        }

        public void Dispose()
        {
            _simulation?.Dispose();
            _threadDispatcher?.Dispose();
            _bufferPool?.Clear();
        }
    }

    // --- BepuPhysics NarrowPhase 콜백 ---

    internal struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex,
            CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
            => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex,
            CollidablePair pair, int childIndexA, int childIndexB)
            => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int workerIndex,
            CollidablePair pair, ref TManifold manifold,
            out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = 0.5f;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30, 1);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int workerIndex,
            CollidablePair pair, int childIndexA, int childIndexB,
            ref ConvexContactManifold manifold)
            => true;

        public void Dispose() { }
    }

    // --- BepuPhysics PoseIntegrator 콜백 (중력 적용) ---

    internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private Vector3 _gravity;
        private Vector3Wide _gravityDtWide;

        public PoseIntegratorCallbacks(Vector3 gravity)
        {
            _gravity = gravity;
            _gravityDtWide = default;
        }

        public readonly AngularIntegrationMode AngularIntegrationMode
            => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            _gravityDtWide = Vector3Wide.Broadcast(_gravity * dt);
        }

        public void IntegrateVelocity(
            Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask,
            int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear += _gravityDtWide;
        }
    }
}
