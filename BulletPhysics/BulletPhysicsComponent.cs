using System;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using VisualPinball.Unity.Game;
using VisualPinball.Unity.VPT.Table;
using VisualPinball.Unity.VPT.Primitive;
using VisualPinball.Unity.VPT.Surface;
using VisualPinball.Unity.VPT.Flipper;
using VisualPinball.Unity.DebugAndPhysicsComunicationProxy;

using Vec3 = BulletSharp.Math.Vector3;
using Matrix = BulletSharp.Math.Matrix;
using SixLabors.ImageSharp.Processing.Processors.Dithering;
using BulletSharp;

namespace VisualPinball.Engine.Unity.BulletPhysics
{
    [AddComponentMenu("Visual Pinball/Bullet Physics Component")]
    [DisallowMultipleComponent]
    public class BulletPhysicsComponent : BulletPhysicsHub, IPhysicsEngine
    {
        // properties
        [SerializeField]
        float _slope = 9.0f;
        public float slope { get { return _slope; } set { _slope = value; base.SetGravity(_gravity, slope); } }

        [SerializeField]
        new float _gravity = 9.81f;
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
        protected override void Awake()
        {
            base.Awake();
            if (enabled)
            {
                // register Bullet Physics Engine
                DPProxy.physicsEngine = this;
                PrepareTable();
            }
        }

        protected void Update()
        {
            base.UpdatePhysics(GetTargetTime());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
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

                foreach (var primitive in table.gameObject.GetComponentsInChildren<PrimitiveBehavior>(true))
                    if (primitive.data.IsCollidable)
                        AddStaticMesh(primitive.gameObject, 0, primitive.data.Friction, primitive.data.Elasticity);

                foreach (var surface in table.gameObject.GetComponentsInChildren<SurfaceBehavior>(true))
                    if (surface.data.IsCollidable)
                        AddStaticMesh(surface.gameObject, 0, surface.data.Friction, surface.data.Elasticity);

            }
        }

        // ==================================================================== IPhysicsEngine ===

        public void OnRegisterFlipper(Entity entity, string name) { /* flipper is registered with PhyFlipperBehaviour */}
        public void OnPhysicsUpdate(int numSteps, float processingTime) { }
        public void OnCreateBall(Entity entity, float3 position, float3 velocity, float radius, float mass) { AddBall(entity, position, velocity, radius, mass); }
        public void OnRotateToEnd(Entity entity) { PhyFlipper.OnRotateToEnd(entity); }
        public void OnRotateToStart(Entity entity) { PhyFlipper.OnRotateToStart(entity); }
        public bool UsePureEntity() { return true; }
        public void ManualBallRoller(Entity entity, float3 targetPosition)
        {
            // right now only last ball is affected
            if (_ballBodyForManualBallRoller != null)
            {
                Vec3 target = _worldToLocal.MultiplyPoint(targetPosition).ToBullet();
                Matrix m;
                _ballBodyForManualBallRoller.GetWorldTransform(out m);
                Vec3 ballPos = ((Vector3)BulletPhysicsExt.ExtractTranslationFromMatrix(ref m)).ToBullet();
                target.Z = ballPos.Z;
                var dir = (target - ballPos);
                dir.Normalize();
                float dist = (target - ballPos).Length * 0.05f;
                _ballBodyForManualBallRoller.AngularVelocity = Vec3.Zero;
                _ballBodyForManualBallRoller.LinearVelocity = Vec3.Zero;
                if (dist > 20) 
                    dist = 20;
                if (dist > 0.1f)
                {
                    dist = dist + 1.0f;
                    _ballBodyForManualBallRoller.ApplyCentralImpulse(dist * dist * dir * (float)10);
                }
            }
        }

        public bool GetFlipperState(Entity entity, out FlipperState flipperState) { return PhyFlipper.GetFlipperState(entity, out flipperState); }
        public float GetFloat(Params param) { return _GetParam(param); }
        public void SetFloat(Params param, float val) { _GetParam(param) = val; }

        private float _dummyFloatParam = 0;
        private ref float _GetParam(Params param)
        {
            switch (param)
            {
                case Params.Physics_FlipperAcc:
                    return ref flipperAcceleration;
                case Params.Physics_FlipperMass:
                    return ref flipperMassMultiplierLog;
                case Params.Physics_FlipperNumOfDegreeNearEnd:
                    return ref flipperNumberOfDegreeNearEnd;
                case Params.Physics_FlipperOffScale:
                    return ref flipperSolenoidOffAccelerationScale;
                case Params.Physics_FlipperOnNearEndScale:
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
        BulletSharp.RigidBody _ballBodyForManualBallRoller = null;

        // ==================================================================== Functions used to add GameObjects to physics engine ===

        void AddPlayfield(TableBehavior table)
        {
            _playfield = new PhyPlayfield(table.Table.Width * 0.5f, table.Table.Height * 0.5f);

            // ToDo: get correct playfield params
            _playfield.SetProperties(
                0,
                table.Table.Data.Friction,
                table.Table.Data.Elasticity * 100.0f);

            var tr = _worldToLocal.MultiplyPoint(UnityEngine.Vector3.zero);
            Add(_playfield, Matrix.Translation(tr.x, tr.y, tr.z));
        }

        void AddStaticMesh(GameObject go, float mass, float friction, float elasticity)
        {
            var meshes = go.GetComponentsInChildren<MeshFilter>(true);
            if (meshes.Length == 0)
                return;

            var body = new PhyStatic(go, mass);
            body.SetProperties(
                mass,
                friction,
                elasticity * 100.0f);

            Add(body, Matrix.Identity);
        }

        void AddFlipper(FlipperBehavior flipper)
        {
            var phyBody = new PhyFlipper(flipper);
            phyBody.SetProperties(
                phyBody.Mass,
                flipper.data.Friction,
                flipper.data.Elasticity * 100.0f);
            
            var phyFlipperBehaviour = flipper.gameObject.AddComponent<PhyFlipperBehaviour>();
            phyFlipperBehaviour.MotionStatePtr = phyBody.GetMotionStateNativePtr();
            phyFlipperBehaviour.RigidBodyIdx = phyBody.RigidBodyIdx;
            phyFlipperBehaviour.Name = flipper.gameObject.name;
            phyFlipperBehaviour.phyBody = phyBody;

            Add(phyBody, flipper.gameObject);
        }

        void AddBall(Entity entity, float3 position, float3 velocity, float radius, float mass)
        {
            var phyBody = new PhyBall(radius, mass);
            phyBody.SetProperties(
                mass,
                0.3f,
                0.012f);
            Add(phyBody, entity, position);

            // last added ball is for manual ball roller
            _ballBodyForManualBallRoller = phyBody.body; 
        }

        // ==================================================================== === ===

        /// <summary>
        /// Register flipper and add BulletPhysicsTransformData component to Entity        
        /// </summary>
        internal class PhyFlipperBehaviour : MonoBehaviour, IConvertGameObjectToEntity
        {
            public string Name;
            public IntPtr MotionStatePtr;
            public int RigidBodyIdx;
            public PhyFlipper phyBody;

            public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
            {
                DPProxy.OnRegisterFlipper(entity, Name);
                PhyFlipper.OnRegiesterFlipper(entity, phyBody);
                dstManager.AddComponentData(entity, new BulletPhysicsTransformData
                {
                    motionStatePtr = MotionStatePtr,
#if UNITY_EDITOR
                    rigidBodyIdx = RigidBodyIdx
#endif
                });
            }
        }
    }
}

/**
 * https://github.com/AndresTraks/BulletSharp/wiki/Collision-Callbacks-and-Triggers
 */
