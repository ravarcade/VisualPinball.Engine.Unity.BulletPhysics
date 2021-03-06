using BulletSharp.Math;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VisualPinball.Engine.Common;
using VisualPinball.Unity.Physics.DebugUI;
using VisualPinball.Unity.VPT.Ball;
using VisualPinball.Unity.VPT.Flipper;
using VisualPinball.Unity.VPT.Gate;
using VisualPinball.Unity.VPT.Primitive;
using VisualPinball.Unity.VPT.Rubber;
using VisualPinball.Unity.VPT.Surface;
using VisualPinball.Unity.VPT.Table;
using Vec3 = BulletSharp.Math.Vector3;
using Vector3 = UnityEngine.Vector3;

namespace VisualPinball.Engine.Unity.BulletPhysics
{
    [AddComponentMenu("Visual Pinball/Bullet Physics Component")]
    [DisallowMultipleComponent]
    public class BulletPhysicsComponent : BulletPhysicsHub
    {
        // properties
        [SerializeField]
        float _slope = 9.0f;
        public float slope { get { return _slope; } set { _slope = value; base.SetGravity(_gravity, slope); } }

        [SerializeField]
        float _gravity = 9.81f;
        public float gravity { get { return _gravity; } set { _gravity = value; base.SetGravity(_gravity, slope); } }

        [Header("Flipper Settings")]
        [SerializeField]
        public float flipperAcceleration = 1.5f;

        [SerializeField]
        [Tooltip("Logaritmic:\n-1=0.1,\n 0=1,\n 1=10,\n 2=100,\n...")]
        public float flipperMassMultiplierLog = 0.0f;

        [SerializeField]
        public float flipperSolenoidOffAccelerationScale = 0.1f;

        [SerializeField]
        public float flipperOnNearEndAccelerationScale = 0.1f;

        [SerializeField]
        public float flipperNumberOfDegreeNearEnd = 5.0f;

        enum TimingMode { RealTime, AtLeast60, Locked60 };
        TimingMode timingMode = TimingMode.AtLeast60;
        float _currentPhysicsTime = 0;

        float GetTargetTime()
        {
            const float dt60fps = 1.0f / 60.0f;
            float t = _currentPhysicsTime + Time.deltaTime;

            switch (timingMode)
            {
                case TimingMode.AtLeast60:
                    float dt = Time.deltaTime;
                    if (dt > dt60fps)
                    {
                        dt = dt60fps;
                    }
                    t = _currentPhysicsTime + dt;
                    break;

                case TimingMode.Locked60:
                    t = _currentPhysicsTime + dt60fps;
                    break;
            }
            return t;
        }

        // ==================================================================== MonoBehaviour  ===

        protected void Awake()
        {
            enabled = false; // will be enabled by BulletPhysics if this is the physics engine.
        }

        protected void Update()
        {
            base.UpdatePhysics(GetTargetTime());
        }

        public void PrepareTable()
        {
            SetGravity(gravity, slope);

            foreach (var table in GameObject.FindObjectsOfType<TableBehavior>())
            {
                // set table transformations
                _localToWorld = table.gameObject.transform.localToWorldMatrix;
                _worldToLocal = table.gameObject.transform.worldToLocalMatrix;

                AddPlayfield(table);

                foreach (var flipper in table.gameObject.GetComponentsInChildren<FlipperBehavior>(true))
                    AddFlipper(flipper);

                foreach (var gate in table.gameObject.GetComponentsInChildren<GateBehavior>(true))
                    AddGate(gate);

                foreach (var primitive in table.gameObject.GetComponentsInChildren<PrimitiveBehavior>(true))
                    if (primitive.data.IsCollidable)
                        AddStaticMesh(primitive.gameObject, 0, primitive.data.Friction, primitive.data.Elasticity);

                foreach (var surface in table.gameObject.GetComponentsInChildren<SurfaceBehavior>(true))
                    if (surface.data.IsCollidable)
                        AddStaticMesh(surface.gameObject, 0, surface.data.Friction, surface.data.Elasticity);

                foreach (var surface in table.gameObject.GetComponentsInChildren<RubberBehavior>(true))
                    if (surface.data.IsCollidable)
                        AddStaticMesh(surface.gameObject, 0, surface.data.Friction, surface.data.Elasticity);

            }
        }

        // ==================================================================== IPhysicsEngine ===

        public void BallCreate(Mesh mesh, Material material, in float3 worldPos, in float3 localPos, in float3 localVel,
            in float scale, in float mass, in float radius)
        {
            var entity = BallManager.CreatePureEntity(mesh, material, worldPos, scale * radius * 2);
            AddBall(entity, localPos, localVel, radius, mass);
            if (EngineProvider<IDebugUI>.Exists)
            {
                EngineProvider<IDebugUI>.Get().OnCreateBall(entity);
            }
        }

        public void OnRotateToEnd(Entity entity) => PhyFlipper.OnRotateToEnd(entity);

        public void OnRotateToStart(Entity entity) => PhyFlipper.OnRotateToStart(entity);

        public void ManualBallRoller(in Entity entity, in float3 targetPosition)
        {
            var ball = PhyBall.Get(entity);
            if (ball != null)
            {
                var body = ball.body;
                Vec3 target = _worldToLocal.MultiplyPoint(targetPosition).ToBullet();
                Matrix m;
                body.GetWorldTransform(out m);
                Vec3 ballPos = ((Vector3)BulletPhysicsExt.ExtractTranslationFromMatrix(ref m)).ToBullet();
                target.Z = ballPos.Z;
                var dir = (target - ballPos);
                dir.Normalize();
                float dist = (target - ballPos).Length * 0.05f;
                body.AngularVelocity = Vec3.Zero;
                body.LinearVelocity = Vec3.Zero;
                if (dist > 20)
                    dist = 20;
                if (dist > 0.1f)
                {
                    dist = dist + 1.0f;
                    body.ApplyCentralImpulse(dist * dist * dir * (float)10);
                }
            }
        }


        public float GetFloat(DebugFlipperSliderParam param) => _GetParam(param);

        public void SetFloat(DebugFlipperSliderParam param, float val) => _GetParam(param) = val;

        private float _dummyFloatParam = 0;

        private ref float _GetParam(DebugFlipperSliderParam param)
        {
            switch (param)
            {
                case DebugFlipperSliderParam.Acc:
                    return ref flipperAcceleration;
                case DebugFlipperSliderParam.Mass:
                    return ref flipperMassMultiplierLog;
                case DebugFlipperSliderParam.NumOfDegreeNearEnd:
                    return ref flipperNumberOfDegreeNearEnd;
                case DebugFlipperSliderParam.OffScale:
                    return ref flipperSolenoidOffAccelerationScale;
                case DebugFlipperSliderParam.OnNearEndScale:
                    return ref flipperOnNearEndAccelerationScale;
            }

            return ref _dummyFloatParam;
        }


        //public void OnDebugDraw()
        //{
        //    if (GetUI_Float != null)
        //    {
        //    	// get params from UI
        //    	flipperAcceleration = GetUI_Float(0, flipperAcceleration);
        //    	flipperMassMultiplierLog = GetUI_Float(1, flipperMassMultiplierLog);
        //    	flipperSolenoidOffAccelerationScale = GetUI_Float(2, flipperSolenoidOffAccelerationScale);
        //    	flipperOnNearEndAccelerationScale = GetUI_Float(3, flipperOnNearEndAccelerationScale);
        //    	flipperNumberOfDegreeNearEnd = GetUI_Float(4, flipperNumberOfDegreeNearEnd);
        //    }
        //}

        // ==================================================================== === ===

        PhyPlayfield _playfield = null;
        //BulletSharp.RigidBody _ballBodyForManualBallRoller = null;

        // ==================================================================== Functions used to add GameObjects to physics engine ===

        void AddPlayfield(TableBehavior table)
        {
            _playfield = new PhyPlayfield(table);
            Add(_playfield);
        }

        void AddStaticMesh(GameObject go, float mass, float friction, float elasticity)
        {
            // no mesh, nothing for physics engine
            var meshes = go.GetComponentsInChildren<MeshFilter>(true);
            if (meshes.Length == 0)
                return;

            var phyBody = new PhyStatic(go, mass, friction, elasticity);
            
            Add(phyBody);
            _HideGameObject(go);
        }

        void AddFlipper(FlipperBehavior flipper)
        {
            var phyBody = new PhyFlipper(flipper);
            Add(phyBody);
            DeferredRegistration(phyBody, flipper.gameObject);
        }

        void AddGate(GateBehavior gate)
        {
            var wires = gate.GetComponentsInChildren<GateWireBehavior>(true);
            var wire = wires?[0];
            if (wire != null && wires.Length == 1)
            {                
                var phyBody = new PhyGate(gate, wire);
                Add(phyBody);
                DeferredRegistration(phyBody, wire.gameObject);
                _RemoveBehavior<GateBehavior>(gate.gameObject);
                _RemoveBehavior<GateWireBehavior>(wire.gameObject);
            }
        }

        void AddBall(Entity entity, float3 position, float3 velocity, float radius, float mass)
        {
            var phyBody = new PhyBall(entity, radius, mass);
            Add(phyBody, position);
        }

    }
}

/**
 * https://github.com/AndresTraks/BulletSharp/wiki/Collision-Callbacks-and-Triggers
 */
