using UnityEngine;
using UnityEngine.InputSystem;

namespace HolyHelicopter
{
    public enum FlightControlMode { Direct, BoundedReticle }

    /// <summary>Immediate steering, moderate world momentum, and a stabilized gun/pilot view.</summary>
    public sealed class FlightController : MonoBehaviour
    {
        // Session only: scene restarts retain the comparison mode without writing player preferences.
        static FlightControlMode sessionMode;
        public FlightControlMode Mode { get; private set; }
        public string ModeLabel => Mode == FlightControlMode.Direct ? "A / DIRECT STEERING" : "B / BOUNDED RETICLE";
        public string ModeHint => Mode == FlightControlMode.Direct ? "F4  A / DIRECT STEERING   /   F5 TUNE" : "F4  B / BOUNDED RETICLE   /   F5 TUNE";
        public Camera Camera { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 Acceleration { get; private set; }
        public Vector2 RequestedMovement { get; private set; }
        public float ClimbCommand { get; private set; }
        public float YawRate { get; private set; }
        public float EngineLoad { get; private set; }
        public float PlanarSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;
        public float Altitude => transform.position.y - SafeGround(transform.position.x, transform.position.z);
        public float Heading => yaw;
        public float TargetHeading => yaw; // Compatibility for angular-command pilots; no queued intent.
        public float LookPitch => -Mathf.Asin(Mathf.Clamp(AimForward.y, -1f, 1f)) * Mathf.Rad2Deg;
        public float PilotPitch => pitch;
        public float Bank { get; private set; }
        public float VisualPitch { get; private set; }
        public Vector2 ReticleOffset => reticle;
        public Vector2 ReticleHalfBounds => new Vector2(Mathf.Clamp(ReticleZone.x, .1f, .4f), Mathf.Clamp(ReticleZone.y, .1f, .4f)) * .5f;
        public Vector3 AimOrigin => Camera ? Camera.transform.position : transform.position;
        public Quaternion AimRotation => Quaternion.LookRotation(AimForward, Vector3.up);
        public Vector3 AimForward
        {
            get
            {
                Vector3 local = Mode == FlightControlMode.BoundedReticle
                    ? new Vector3(reticle.x * HorizontalProjection, reticle.y * VerticalProjection, 1f).normalized
                    : Vector3.forward;
                return Quaternion.Euler(pitch, yaw, 0) * local;
            }
        }
        public Vector3 AimRight => AimRotation * Vector3.right;
        public Vector3 AimUp => AimRotation * Vector3.up;
        public Vector3 LocalVelocity => Quaternion.Euler(0, -yaw, 0) * Velocity;
        public float CockpitViewBank => Bank * CameraBankMotion * CameraMotion;
        public float CockpitViewPitch => VisualPitch * CameraPitchMotion * CameraMotion;

        [Header("Mouse / stabilized pilot optics")]
        public float Sensitivity = .10f;
        public bool InvertY;
        public float DirectYawResponse = 1f;
        [Range(0f, 1f)] public float CameraMotion = .7f;
        [Range(0f, .6f)] public float CameraBankMotion = .3f;
        [Range(0f, 2f)] public float CameraPitchMotion = .8f;
        [Header("Cruise speeds (m/s)")]
        public float ForwardCruise = 23f;
        public float StrafeCruise = 18f;
        public float ReverseCruise = 16f;
        [Header("Velocity response (m/s squared; no input or jerk delay)")]
        public float ThrustAcceleration = 28f;
        public float ReleaseBraking = 46f;
        public float ReversalAcceleration = 52f;
        public float StrafeAcceleration = 34f;
        [Header("Bounded steering (rate responds immediately)")]
        public float MaximumYawRate = 160f;
        [Range(0f, .4f)] public float SteeringDeadZone = .12f;
        [Range(.5f, 2f)] public float SteeringExponent = 1.25f;
        public Vector2 ReticleZone = new Vector2(.20f, .20f);
        [Header("Physical presentation (does not steer the gun)")]
        public float MaximumBank = 13f;
        public float MaximumBodyPitch = 10f;
        public float BodyResponse = 9f;
        [Header("Altitude hold")]
        public float MinimumClearance = 3.4f;
        public float MaximumAltitude = 58f;
        public float ClimbSpeed = 10f;
        public float DescendSpeed = 8f;
        public float VerticalAcceleration = 32f;

        float yaw = -22f, pitch = 19f, verticalSpeed, holdHeight, previousClimb;
        Vector2 reticle;
        Vector3 cameraOffset;
        Quaternion viewHandoff = Quaternion.identity;
        bool discardMouse = true;
        Game game;
        Transform body, rotor;
        Light searchlight;
        float VerticalProjection => 2f * Mathf.Tan(39f * Mathf.Deg2Rad);
        float HorizontalProjection => VerticalProjection * (Camera ? Camera.aspect : 16f / 9f);

        public void Initialize(Game owner)
        {
            game = owner;
            Mode = sessionMode;
            transform.position = World.BasePosition + Vector3.up * 9f;
            holdHeight = transform.position.y;
            var view = new GameObject("Pilot view / stabilized gun optics");
            view.transform.SetParent(transform, false);
            Camera = view.AddComponent<Camera>();
            Camera.tag = "MainCamera";
            Camera.fieldOfView = 78f;
            Camera.nearClipPlane = .12f;
            Camera.farClipPlane = 2600f;
            Camera.backgroundColor = new Color(.025f, .042f, .075f);
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.cullingMask = ~(1 << 8);
            view.AddComponent<AudioListener>();
            var lamp = new GameObject("Wide holy searchlight");
            lamp.transform.SetParent(view.transform, false);
            searchlight = lamp.AddComponent<Light>();
            searchlight.type = LightType.Spot;
            searchlight.color = new Color(.63f, .88f, 1f);
            searchlight.intensity = 7f;
            searchlight.range = 135f;
            searchlight.spotAngle = 55f;
            searchlight.innerSpotAngle = 25f;
            searchlight.shadows = LightShadows.None;
            BuildAirframe();
            ApplyView(0, true);
        }

        public void ToggleMode() => SetMode(Mode == FlightControlMode.Direct ? FlightControlMode.BoundedReticle : FlightControlMode.Direct);

        public void SetMode(FlightControlMode mode)
        {
            if (Mode == mode) return;
            Quaternion oldView = Camera ? Camera.transform.rotation : Quaternion.Euler(pitch, yaw, 0);
            Vector3 oldEye = AimOrigin;
            Vector3 aim = AimForward;
            // Trim to the existing gun bearing, retaining world velocity and the exact sight.
            // The eye eases the small off-centre trim over ~0.2s; no queued steering.
            yaw = Mathf.Repeat(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, 360f);
            pitch = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
            reticle = Vector2.zero;
            Mode = sessionMode = mode;
            YawRate = 0;
            viewHandoff = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0)) * oldView;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cameraOffset = transform.InverseTransformPoint(oldEye);
            ApplyView(0, false);
        }

        // Ignore cursor-warp deltas on recapture. Park the reticle while chart/menus own the mouse.
        public void SuspendInput() { discardMouse = true; YawRate = 0; }

        public void Tick(float dt, bool acceptInput)
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 movement = Vector2.zero;
            float climb = 0, turn = 0, look = 0, scroll = 0;
            if (acceptInput)
            {
                if (keys != null)
                {
                    movement.x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                    movement.y = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                    climb = (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0);
                }
                if (mouse != null && !discardMouse)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    turn = delta.x * Sensitivity;
                    look = delta.y * Sensitivity * (InvertY ? 1f : -1f);
                    float wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > .01f) scroll = Mathf.Sign(wheel);
                }
                discardMouse = false;
            }
            else discardMouse = true;
            TickInternal(dt, movement, climb, turn, look, scroll, acceptInput);
        }

        /// <summary>
        /// Shared mouse/pilot path: normalized movement/climb, angular mouse displacement in degrees
        /// (positive look down), signed altitude notches. In B these deltas move the bounded sight;
        /// held displacement drives yaw RATE. Scripted heading pilots select A explicitly.
        /// </summary>
        public void TickCommand(float dt, Vector2 movement, float climb, float yawInput, float lookInput = 0f, float scroll = 0f)
            => TickInternal(dt, movement, climb, yawInput, lookInput, scroll, true);

        void TickInternal(float dt, Vector2 movement, float climb, float yawInput, float lookInput, float scroll, bool steer)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            RequestedMovement = Vector2.ClampMagnitude(movement, 1f);
            ClimbCommand = Mathf.Clamp(climb, -1f, 1f);
            float yawDelta, startReticleAngle = 0;
            if (Mode == FlightControlMode.Direct)
            {
                yawDelta = Mathf.Clamp(yawInput * DirectYawResponse, -180f, 180f);
                pitch = Mathf.Clamp(pitch + lookInput, -28f, 78f);
            }
            else
            {
                Vector2 half = ReticleHalfBounds;
                float xAngle = Mathf.Atan(reticle.x * HorizontalProjection) * Mathf.Rad2Deg;
                startReticleAngle = xAngle;
                reticle.x = Mathf.Tan(Mathf.Clamp(xAngle + yawInput, -60f, 60f) * Mathf.Deg2Rad) / HorizontalProjection;
                reticle.x = Mathf.Clamp(reticle.x, -half.x, half.x);
                float yAngle = Mathf.Atan(reticle.y * VerticalProjection) * Mathf.Rad2Deg - lookInput;
                float limit = Mathf.Atan(half.y * VerticalProjection) * Mathf.Rad2Deg;
                float bounded = Mathf.Clamp(yAngle, -limit, limit);
                // Crossing a vertical edge looks through the lower/upper window immediately.
                // Elevation never drives aircraft pitch or continues after the mouse stops.
                pitch = Mathf.Clamp(pitch + bounded - yAngle, -28f + bounded, 78f + bounded);
                reticle.y = Mathf.Tan(bounded * Mathf.Deg2Rad) / VerticalProjection;
                yawDelta = 0; // Integrate the mouse displacement at the same 120Hz as movement below.
            }
            YawRate = yawDelta / dt;
            holdHeight += Mathf.Clamp(scroll, -8f, 8f) * 3.5f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float turn = yawDelta / steps;
                if (Mode == FlightControlMode.BoundedReticle)
                {
                    // Distribute this frame's mouse sample across substeps. A continuous device motion
                    // then turns the same amount at 30/60/120Hz, with no residual intent after the tick.
                    float angle = Mathf.Clamp(startReticleAngle + yawInput * ((i + 1f) / steps), -60f, 60f);
                    float x = Mathf.Clamp(Mathf.Tan(angle * Mathf.Deg2Rad) / HorizontalProjection / ReticleHalfBounds.x, -1, 1);
                    float rate = Mathf.Pow(Mathf.Clamp01((Mathf.Abs(x) - SteeringDeadZone) / Mathf.Max(.01f, 1 - SteeringDeadZone)), SteeringExponent);
                    YawRate = steer ? Mathf.Sign(x) * rate * MaximumYawRate : 0;
                    turn = YawRate * step;
                }
                yaw = Mathf.Repeat(yaw + turn, 360f);
                Integrate(step);
            }
            viewHandoff = Quaternion.Slerp(viewHandoff, Quaternion.identity, 1f - Mathf.Exp(-dt * 18f));
            ApplyView(dt, false);
            if (body) body.localRotation = Quaternion.Euler(VisualPitch, 0, Bank);
            if (rotor) rotor.Rotate(Vector3.up, dt * (1720f + EngineLoad * 190f), Space.Self);
        }

        void Integrate(float dt)
        {
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            float fuelPower = game.Fuel > 0 ? 1f : .3f;
            Vector3 desired = heading * new Vector3(RequestedMovement.x * StrafeCruise, 0,
                RequestedMovement.y * (RequestedMovement.y >= 0 ? ForwardCruise : ReverseCruise)) * fuelPower;
            Vector3 previousPosition = transform.position;
            Vector3 planar = new Vector3(Velocity.x, 0, Velocity.z);
            Vector3 previousVelocity = Velocity;
            bool braking = RequestedMovement.sqrMagnitude < .001f;
            bool reversing = Vector3.Dot(planar, desired) < -.01f;
            float acceleration = braking ? ReleaseBraking : reversing ? ReversalAcceleration :
                Mathf.Lerp(ThrustAcceleration, StrafeAcceleration, Mathf.Abs(RequestedMovement.x));
            // Acceleration changes immediately. Momentum remains in velocity, never an input backlog.
            planar = Vector3.MoveTowards(planar, desired, Mathf.Max(.1f, acceleration) * (braking ? 1f : fuelPower) * dt);
            Vector3 next = previousPosition + planar * dt;
            float boundary = World.Radius - 12f;
            if (Mathf.Abs(next.x) > boundary) { next.x = Mathf.Clamp(next.x, -boundary, boundary); planar.x = 0; }
            if (Mathf.Abs(next.z) > boundary) { next.z = Mathf.Clamp(next.z, -boundary, boundary); planar.z = 0; }
            float ground = SafeGround(next.x, next.z);
            // Altitude commands move/brake now; retain absolute hold for scroll and terrain safety.
            if (Mathf.Abs(ClimbCommand) > .001f) holdHeight = next.y + ClimbCommand * (ClimbCommand >= 0 ? ClimbSpeed : DescendSpeed) / 3.5f;
            else if (Mathf.Abs(previousClimb) > .001f) holdHeight = next.y;
            previousClimb = ClimbCommand;
            holdHeight = Mathf.Clamp(holdHeight, ground + MinimumClearance, ground + MaximumAltitude);
            float safeFloor = ground + MinimumClearance;
            for (int sample = 1; sample <= 4; sample++)
            {
                Vector3 ahead = next + planar * (sample * .42f);
                safeFloor = Mathf.Max(safeFloor, SafeGround(ahead.x, ahead.z) + MinimumClearance);
            }
            holdHeight = Mathf.Max(holdHeight, safeFloor);
            if (game.Fuel <= 0) holdHeight = Mathf.Max(ground + MinimumClearance, holdHeight - dt * 2f);
            float desiredVertical = Mathf.Clamp((holdHeight - next.y) * 3.5f, -DescendSpeed, ClimbSpeed + 4f);
            verticalSpeed = Mathf.MoveTowards(verticalSpeed, desiredVertical, VerticalAcceleration * dt);
            next.y += verticalSpeed * dt;
            if ((holdHeight - previousPosition.y) * (holdHeight - next.y) <= 0 && Mathf.Abs(ClimbCommand) < .001f)
            { next.y = holdHeight; verticalSpeed = 0; }
            next.y = Mathf.Clamp(next.y, ground + MinimumClearance, ground + MaximumAltitude);
            transform.position = next;
            Velocity = new Vector3(planar.x, (next.y - previousPosition.y) / Mathf.Max(.0001f, dt), planar.z);
            Acceleration = (Velocity - previousVelocity) / Mathf.Max(.0001f, dt);
            Vector3 localAcceleration = Quaternion.Inverse(heading) * Acceleration;
            Vector3 localVelocity = Quaternion.Inverse(heading) * Velocity;
            float speedRatio = Mathf.Clamp01(PlanarSpeed / ForwardCruise);
            float desiredPitch = Mathf.Clamp(localAcceleration.z * .22f + localVelocity.z / ForwardCruise * 4f, -MaximumBodyPitch, MaximumBodyPitch);
            float desiredBank = Mathf.Clamp(-localAcceleration.x * .25f - localVelocity.x / StrafeCruise * 6f -
                Mathf.Clamp(YawRate / Mathf.Max(1, MaximumYawRate), -1, 1) * (3f + speedRatio * 4f), -MaximumBank, MaximumBank);
            float response = 1f - Mathf.Exp(-dt * BodyResponse);
            VisualPitch = Mathf.Lerp(VisualPitch, desiredPitch, response);
            Bank = Mathf.Lerp(Bank, desiredBank, response);
            float load = .18f + RequestedMovement.magnitude * .35f + Mathf.Abs(ClimbCommand) * .25f +
                         Mathf.Clamp01(new Vector2(Acceleration.x, Acceleration.z).magnitude / Mathf.Max(1, ThrustAcceleration)) * .22f;
            EngineLoad = Mathf.Lerp(EngineLoad, Mathf.Clamp01(load), response);
        }

        static float SafeGround(float x, float z) => Mathf.Max(World.Height(x, z), World.SurfaceHeight(x, z));

        void ApplyView(float dt, bool snap)
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (!Camera) return;
            float response = snap ? 1f : 1f - Mathf.Exp(-dt * 10f);
            Vector3 localAcceleration = Quaternion.Euler(0, -yaw, 0) * Acceleration;
            Vector3 offset = Vector3.ClampMagnitude(new Vector3(-localAcceleration.x, -Acceleration.y * .5f, -localAcceleration.z) * .001f, .035f) * CameraMotion;
            cameraOffset = Vector3.Lerp(cameraOffset, offset, response);
            if (CameraMotion <= 0) cameraOffset = Vector3.zero;
            Camera.transform.localPosition = cameraOffset;
            // Body/canopy bank and pitch carry the machine's motion; the gun optics stay stabilized.
            Camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0) * viewHandoff;
            Camera.fieldOfView = 78f;
            if (searchlight) { searchlight.transform.position = AimOrigin; searchlight.transform.rotation = AimRotation; }
            Vector3 p = AimOrigin;
            Shader.SetGlobalVector("_HolyLampPosition", new Vector4(p.x, p.y, p.z, 1));
            Shader.SetGlobalVector("_HolyLampDirection", AimForward);
        }

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            holdHeight = position.y;
            Velocity = Acceleration = cameraOffset = Vector3.zero;
            RequestedMovement = Vector2.zero;
            ClimbCommand = YawRate = Bank = VisualPitch = previousClimb = verticalSpeed = 0;
            reticle = Vector2.zero;
            viewHandoff = Quaternion.identity;
            ApplyView(0, true);
        }

        public void Teleport(Vector3 position, float heading, float lookPitch)
        {
            SetAim(heading, lookPitch);
            Teleport(position);
        }

        /// <summary>Initial/test pose only; ordinary steering goes through TickCommand.</summary>
        public void SetAim(float heading, float lookPitch)
        {
            yaw = Mathf.Repeat(heading, 360f);
            pitch = Mathf.Clamp(lookPitch, -28f, 78f);
            YawRate = 0;
            reticle = Vector2.zero;
            viewHandoff = Quaternion.identity;
            ApplyView(0, true);
        }

        void BuildAirframe()
        {
            body = new GameObject("Black canary airframe").transform;
            body.SetParent(transform, false);
            Material black = World.Mat("Airframe / charcoal", new Color(.025f, .035f, .04f));
            Material yellow = World.Mat("Consecrated rotor yellow", new Color(1f, .73f, .08f), true);
            Material glass = World.Mat("Armored blue canopy", new Color(.08f, .23f, .27f));
            Part(PrimitiveType.Cube, "Armored fuselage", new Vector3(0, -.8f, -.5f), new Vector3(2.5f, 1.9f, 4.5f), black);
            Part(PrimitiveType.Sphere, "Bubble canopy", new Vector3(0, -.1f, 1f), new Vector3(2.4f, 1.7f, 2.7f), glass);
            Part(PrimitiveType.Cube, "Tail boom", new Vector3(0, -.4f, -4.3f), new Vector3(.45f, .6f, 5.3f), black);
            for (int side = -1; side <= 1; side += 2)
                Part(PrimitiveType.Cube, "Landing skid", new Vector3(side * 1.6f, -2f, -.3f), new Vector3(.16f, .16f, 5.3f), black);
            rotor = new GameObject("Yellow rotating crucifix").transform;
            rotor.SetParent(body, false);
            rotor.localPosition = Vector3.up * 1.4f;
            var a = World.Shape(PrimitiveType.Cube, "Cross blade A", rotor, rotor.position, new Vector3(12f, .09f, .5f), yellow);
            var b = World.Shape(PrimitiveType.Cube, "Cross blade B", rotor, rotor.position, new Vector3(.5f, .09f, 12f), yellow);
            a.transform.localPosition = b.transform.localPosition = Vector3.zero;
            foreach (Transform t in body.GetComponentsInChildren<Transform>()) t.gameObject.layer = 8;
        }

        void Part(PrimitiveType type, string name, Vector3 local, Vector3 scale, Material mat)
        {
            var go = World.Shape(type, name, body, body.TransformPoint(local), scale, mat);
            go.transform.localPosition = local;
        }
    }
}
