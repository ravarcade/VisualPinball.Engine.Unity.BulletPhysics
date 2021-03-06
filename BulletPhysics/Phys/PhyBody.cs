using BulletSharp;
using BulletSharp.Math;
using Unity.Entities;
using UnityEngine;
using Vector3 = BulletSharp.Math.Vector3;

namespace VisualPinball.Engine.Unity.BulletPhysics
{
    public enum PhyType
    {
        Playfield = 0,
        Ball = 1,
        Static = 2,
        Flipper = 3,
        Gate = 4,
        Everything = 0x7fff
    };

    /// <summary>
    /// Base class for all Phy[something] objects.
    /// </summary>
    public class PhyBody
    {
        private PhyType _phyType;
        private bool _lockPosition;
        private CollisionObject _collisionObject;
        private string _name = "";
        private Entity _entity = Entity.Null;
        private Vector3 _offset = Vector3.Zero;
        private Matrix _matrix = Matrix.Identity;
        private Matrix4x4 _localToWorld = Matrix4x4.identity;
        private bool _isLocalToWorldSet = false;

        public object userObject { get; set; }
        public string name { get => _name; protected set { _name = value; } }
        public Entity entity { get => _entity; protected set { _entity = value; } }
        public Vector3 offset { get => _offset; protected set { _offset = value; } }
        
        public bool isLocalToWorldSet { get => _isLocalToWorldSet; }
        public Matrix4x4 localToWorld { get => _localToWorld; protected set { _localToWorld = value; _isLocalToWorldSet = true; } }
        public Matrix matrix { get => _matrix; protected set { _matrix = value; } }

        /// <summary>
        /// Base class for all Phy[something] objects.
        /// </summary>
        /// <param name="phyType">Type of object in bullet physics.</param>
        protected PhyBody(PhyType phyType = PhyType.Static, bool lockPosition = true)
        {
            _phyType = phyType;
            _lockPosition = lockPosition;
        }

        public void SetupRigidBody(float mass, CollisionShape shape, float margin = 0.04f)
        {
            shape.Margin = margin;

            var constructionInfo = new RigidBodyConstructionInfo(
                    mass,
                    CreateMotionState(),
                    shape);

            _collisionObject = new RigidBody(constructionInfo);
        }

        public short phyType { get { return (short)_phyType; } }
        public bool lockPosition { get => _lockPosition; }

        public RigidBody body { get { return (RigidBody)_collisionObject; } }

        public virtual TypedConstraint Constraint { get { return null; } }

        public virtual void Register(Entity entity) { _entity = entity; }

        public void SetProperties(float mass, float friction, float restitution)
        {
            if (_collisionObject != null)
            {
                _collisionObject.Friction = friction;
                _collisionObject.Restitution = restitution;

                body.Friction = friction;
                body.Restitution = restitution;

                if (mass > 0)
                {
                    Vector3 inertia = body.CollisionShape.CalculateLocalInertia(mass);
                    body.SetMassProps(mass, inertia);
                }
            }
        }

        public static MotionState CreateMotionState() { return new DefaultMotionState(); }

        public void SetWorldTransform(Matrix m)
        {
            body.MotionState.SetWorldTransform(ref m);
            body.WorldTransform = m;
        }

        public MotionStateNativeView ToView()
        {
            return body.MotionState.ToView(_offset);
        }
    }
}
