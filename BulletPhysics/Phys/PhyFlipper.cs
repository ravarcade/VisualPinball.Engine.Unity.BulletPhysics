using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using VisualPinball.Unity.VPT.Flipper;
using VisualPinball.Unity.Extensions;
using BulletSharp;
using VisualPinball.Unity.Physics.DebugUI;
using Vector3 = BulletSharp.Math.Vector3;
using Matrix = BulletSharp.Math.Matrix;
using VisualPinball.Engine.Common;
using FluentAssertions.Extensions;

namespace VisualPinball.Engine.Unity.BulletPhysics
{
    public class PhyFlipper : PhyBody
    {

        public float Mass;
        public int SolenoidState = 0;
        public int RotationDirection = 1;  /// left flipper =-1, right flipper =+1

        TypedConstraint _constraint = null;
        float _height;
        float _startAngle;
        float _endAngle;

        float _usedFlipperMass = 1.0f;
        float _prevFlipperMassMultiplierLog = float.MaxValue;

        readonly static Dictionary<Entity, PhyFlipper> _flippers = new Dictionary<Entity, PhyFlipper>();

        public PhyFlipper(FlipperBehavior flipper) : base(PhyType.Flipper)
        {
            Mass = flipper.data.Mass * 3.0f;
            RotationDirection = flipper.data.StartAngle > flipper.data.EndAngle ? -1 : 1;
            _height = flipper.data.Height;
            _startAngle = flipper.data.StartAngle * Mathf.PI / 180.0f;
            _endAngle = flipper.data.EndAngle * Mathf.PI / 180.0f;

            SetupRigidBody(flipper.data.Mass, _AddFlipperCylinders(flipper));
            
            SetProperties(
                Mass,
                flipper.data.Friction,
                flipper.data.Elasticity * 100.0f);


            base.matrix = Matrix4x4.TRS(
                flipper.gameObject.transform.localPosition,
                flipper.gameObject.transform.localRotation,
                UnityEngine.Vector3.one
                ).ToBullet();

            base.name = flipper.name;
            base.entity = Entity.Null;
        }

        public override void Register(Entity entity)
        {
            // Flipper is alredy registered in DebugUI, see: VisualPinball.Unity.Game.Player.RegisterFlipper
            // if (EngineProvider<IDebugUI>.Exists)
            //    EngineProvider<IDebugUI>.Get().OnRegisterFlipper(entity, Name);

            SolenoidState = -1; // down
            _flippers[entity] = this;
            base.Register(entity);

            _AddDebugProperties();
        }

        // Adding hinge should be done after RigidBody is added to world
        public override TypedConstraint Constraint
        {
            get
            {
                if (_constraint == null)
                {
                    _constraint = _AddFlipperHinge();
                }
                return _constraint;
            }
        }

        TypedConstraint _AddFlipperHinge()
        {
            float hh = _height * 0.5f;

            var hinge = new HingeConstraint(
                body,
                new Vector3(0, 0, hh),
                Vector3.UnitZ,
                false);

            body.ActivationState = ActivationState.DisableDeactivation;
            body.SetSleepingThresholds(float.MaxValue, 0.0f); // no sleep for flippers

            if (RotationDirection == 1)
            {
                hinge.SetLimit(_startAngle, _endAngle, 0.0f);
            }
            else
            {
                hinge.SetLimit(_endAngle, _startAngle, 0.0f);
            }

            return hinge;
        }

        CollisionShape _AddFlipperCylinders(FlipperBehavior flipper)
        {
            float r1 = flipper.data.BaseRadius;
            float r2 = flipper.data.EndRadius;
            float h = flipper.data.Height;
            float l = flipper.data.FlipperRadius;

            var hh = h * 0.5f; // half height
            var cs = new BulletSharp.CompoundShape();

            cs.AddChildShape(
                Matrix.Translation(0, 0, hh),
                new CylinderShapeZ(r1, r1, hh));

            cs.AddChildShape(
                Matrix.Translation(0, -l, hh),
                new CylinderShapeZ(r2, r2, hh));

            // we can't add Triangle Mesh Shape to Compound Shape. Add one or two boxes
            float hbl = new Vector2(l, r1 - r2).magnitude * 0.5f;
            Vector3 n = new Vector3(l, r1 - r2, 0); n.Normalize();
            Vector3 beg = new Vector3(0, 0, hh) + n * (r1 - r2);
            Vector3 beg2 = new Vector3(-beg.X, beg.Y, beg.Z);
            Vector3 end = new Vector3(0, -l, hh);
            float angle = math.atan2(n.Y, n.X);

            bool onlyFront = true;
            bool rev = (flipper.data.StartAngle < 0 | flipper.data.StartAngle > 180);

            if (!onlyFront || rev)
                cs.AddChildShape(
                    Matrix.RotationZ(-angle) *
                    Matrix.Translation((beg + end) * 0.5f),
                    new BoxShape(Mathf.Min(r1, r2), hbl, hh));

            if (!onlyFront || !rev)
                cs.AddChildShape(
                    Matrix.RotationZ(angle) *
                    Matrix.Translation((beg2 + end) * 0.5f),
                    new BoxShape(Mathf.Min(r1, r2), hbl, hh));

            return cs;
        }

        /// <summary>
        /// Flipper rotation.
        /// Executed every physics simulation step.
        /// </summary>
        void _FlipperUpdate(ref BulletPhysicsComponent bpc)
        {
            _UpdateFlipperMass(ref bpc);
            float M = (float)(_usedFlipperMass * 1e7);
            float angle = _GetAngle();
            float maxAngle = math.abs(_startAngle - _endAngle) * (180.0f / math.PI);
            float flipperOnForce = bpc.flipperAcceleration;
            float flipperOffForce = flipperOnForce * bpc.flipperSolenoidOffAccelerationScale;
            if (angle > (maxAngle - bpc.flipperNumberOfDegreeNearEnd) && SolenoidState == 1)
            {
                flipperOnForce *= bpc.flipperOnNearEndAccelerationScale;
            }

            if (angle < bpc.flipperNumberOfDegreeNearEnd && SolenoidState == -1)
            {
                flipperOffForce *= bpc.flipperOnNearEndAccelerationScale;
            }

            switch (SolenoidState)
            {
                case 1:
                    body.ApplyTorque(new Vector3(0, 0, RotationDirection) * flipperOnForce * M);
                    break;
                case -1:
                    body.ApplyTorque(new Vector3(0, 0, -RotationDirection) * flipperOffForce * M);
                    break;
            }
        }

        float _GetAngle()
        {
            Matrix m = body.WorldTransform;
            float3 rot = BulletPhysicsExt.ExtractRotationFromMatrix(ref m).ToEuler();
            return math.abs(rot.z - _startAngle) * (180.0f / math.PI);
        }

        void _UpdateFlipperMass(ref BulletPhysicsComponent bpc)
        {
            // Update params from ImGui menu
            float newMass = math.pow(10.0f, bpc.flipperMassMultiplierLog);
            if (newMass != _prevFlipperMassMultiplierLog)
            {
                _prevFlipperMassMultiplierLog = newMass;
                _usedFlipperMass = Mass * _prevFlipperMassMultiplierLog;
                Vector3 inertia = body.CollisionShape.CalculateLocalInertia(_usedFlipperMass);
                body.SetMassProps(_usedFlipperMass, inertia);
            }
        }
 
        // ========================================================================== Static Methods & Data ===

        public static void OnRotateToEnd(Entity entity) { _flippers[entity].SolenoidState = 1; }
        public static void OnRotateToStart(Entity entity) { _flippers[entity].SolenoidState = -1; }

        public static DebugFlipperState[] GetFlipperStates()
        {
            var states = new DebugFlipperState[_flippers.Count];
            var i = 0;
            foreach (var entity in _flippers.Keys)
            {
                var flipper = _flippers[entity];
                states[i] = new DebugFlipperState(entity, flipper._GetAngle(), flipper.SolenoidState == 1);
                i++;
            }

            return states;
        }

        public static void Update(ref BulletPhysicsComponent bpc)
        {
            foreach (var entry in _flippers)
            {
                PhyFlipper phyFlipper = entry.Value;
                phyFlipper._FlipperUpdate(ref bpc);

                // debug
                var dbg = EngineProvider<IDebugUI>.Get();
                bool isChanged = false;
                float sa = phyFlipper._startAngle * 180.0f / Mathf.PI;
                float se = phyFlipper._endAngle * 180.0f / Mathf.PI;

                isChanged |= dbg.GetProperty(phyFlipper.dbgPropStartAngle, ref sa);
                isChanged |= dbg.GetProperty(phyFlipper.dbgPropEndAngle, ref se);

                if (isChanged) 
                {
                    phyFlipper._startAngle = sa * Mathf.PI / 180.0f;
                    phyFlipper._endAngle = se * Mathf.PI / 180.0f;

                    HingeConstraint hinge = (HingeConstraint)phyFlipper._constraint;
                    if (phyFlipper.RotationDirection == 1)
                    {
                        hinge.SetLimit(phyFlipper._startAngle, phyFlipper._endAngle, 0.0f);
                    }
                    else
                    {
                        hinge.SetLimit(phyFlipper._endAngle, phyFlipper._startAngle, 0.0f);
                    }
                }
                
            }
        }

        static int dbgFlippersRoot = -1;
        int dbgThisFlipper = -1;
        int dbgPropStartAngle = -1;
        int dbgPropEndAngle = -1;

        void _AddDebugProperties()
        {
            var dbg = EngineProvider<IDebugUI>.Get();
            if (dbgFlippersRoot == -1)
                dbgFlippersRoot = dbg.AddProperty(-1, "Flippers Props", this);

            dbgThisFlipper = dbg.AddProperty(dbgFlippersRoot, base.name, this);
            dbgPropStartAngle = dbg.AddProperty(dbgThisFlipper, "Start Angle", _startAngle * 180.0f / Mathf.PI);
            dbgPropEndAngle = dbg.AddProperty(dbgThisFlipper, "End Angle", _endAngle * 180.0f / Mathf.PI);
        }

    }
}
