using UnityEngine;
using UnityEngine.InputSystem;

namespace HolyHelicopter
{
    /// <summary>Arcade thrust with world-space momentum and an independently stabilized gun sight.</summary>
    public sealed class FlightController : MonoBehaviour
    {
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
        public float TargetHeading => targetYaw;
        public float LookPitch => pitch;
        public float Bank { get; private set; }
        public float VisualPitch { get; private set; }
        public Vector3 AimOrigin => transform.position;
        public Quaternion AimRotation => Quaternion.Euler(pitch, yaw, 0);
        public Vector3 AimForward => AimRotation * Vector3.forward;
        public Vector3 AimRight => AimRotation * Vector3.right;
        public Vector3 AimUp => AimRotation * Vector3.up;
        public Vector3 LocalVelocity => Quaternion.Euler(0, -yaw, 0) * Velocity;

        [Header("Pilot comfort")]
        public float Sensitivity = .085f;
        public bool InvertY;
        [Range(0f, 1f)] public float CameraMotion = .85f;
        [Header("Cruise speeds (m/s)")]
        public float ForwardCruise = 23f;
        public float StrafeCruise = 18f;
        public float ReverseCruise = 14f;
        [Header("Thrust and airbrake")]
        public float ThrustAcceleration = 8.8f;
        public float ReleaseBraking = 13.5f;
        public float ThrustJerk = 36f;
        public float BrakeJerk = 45f;
        [Header("Heading servo (degrees)")]
        public float MaximumYawRate = 100f;
        public float YawAcceleration = 240f;
        [Header("Altitude hold")]
        public float MinimumClearance = 3.4f;
        public float MaximumAltitude = 58f;
        public float ClimbSpeed = 10f;
        public float DescendSpeed = 8f;

        float yaw = -22f, targetYaw = -22f, pitch = 19f, verticalSpeed, holdHeight;
        float cameraRoll, cameraPitch;
        Vector3 thrust, cameraOffset;
        Game game;
        Transform body, rotor;
        Light searchlight;

        public void Initialize(Game owner)
        {
            game = owner;
            transform.position = World.BasePosition + Vector3.up * 9f;
            holdHeight = transform.position.y;
            var view = new GameObject("Pilot view — independent aiming");
            view.transform.SetParent(transform, false);
            Camera = view.AddComponent<Camera>();
            Camera.tag = "MainCamera";
            Camera.fieldOfView = 78f;
            Camera.nearClipPlane = .12f;
            Camera.farClipPlane = 2600f;
            Camera.backgroundColor = new Color(.025f, .042f, .075f);
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.cullingMask = ~(1 << 8); // The physical helicopter casts a silhouette; pilot sees the HUD canopy.
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

        public void Tick(float dt, bool acceptInput)
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 movement = Vector2.zero;
            float climb = 0f, turn = 0f, look = 0f, scroll = 0f;
            if (acceptInput)
            {
                if (keys != null)
                {
                    movement.x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                    movement.y = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                    climb = (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0);
                }
                if (mouse != null)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    turn = delta.x * Sensitivity;
                    look = delta.y * Sensitivity * (InvertY ? 1f : -1f);
                    float wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > .01f) scroll = Mathf.Sign(wheel);
                }
            }
            TickCommand(dt, movement, climb, turn, look, scroll);
        }

        /// <summary>
        /// The actual player input and automated pilot use this same path. Movement/climb are
        /// normalized commands. Yaw/look inputs are angular DISPLACEMENTS in degrees this tick,
        /// not rates; a continuous 60-degree/second turn supplies 60*dt. Positive look aims down.
        /// Scroll is signed notch count. Heading intent is retained while the angular servo catches up.
        /// </summary>
        public void TickCommand(float dt, Vector2 movement, float climb, float yawInput, float lookInput = 0f, float scroll = 0f)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            RequestedMovement = Vector2.ClampMagnitude(movement, 1f);
            ClimbCommand = Mathf.Clamp(climb, -1f, 1f);
            // Cap accumulated turn intent so a single pathological device event cannot queue spins.
            targetYaw = yaw + Mathf.Clamp(Mathf.DeltaAngle(yaw, targetYaw + yawInput), -135f, 135f);
            pitch = Mathf.Clamp(pitch + lookInput, -28f, 78f);
            holdHeight += Mathf.Clamp(scroll, -8f, 8f) * 3.5f;
            // Fixed maximum integration step keeps acceleration and clearance consistent at 30/60/120 Hz.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++) Integrate(step);
            ApplyView(dt, false);
            if (body) body.localRotation = Quaternion.Euler(VisualPitch, 0, Bank);
            if (rotor) rotor.Rotate(Vector3.up, dt * (1720f + EngineLoad * 190f), Space.Self);
        }

        void Integrate(float dt)
        {
            float headingError = Mathf.DeltaAngle(yaw, targetYaw);
            float stoppingRate = Mathf.Sqrt(2f * YawAcceleration * Mathf.Abs(headingError));
            float desiredYawRate = Mathf.Sign(headingError) * Mathf.Min(MaximumYawRate, Mathf.Min(stoppingRate, Mathf.Abs(headingError) * 5f));
            YawRate = Mathf.MoveTowards(YawRate, desiredYawRate, YawAcceleration * dt);
            yaw = Mathf.Repeat(yaw + YawRate * dt, 360f);
            if (Mathf.Abs(headingError) < .005f && Mathf.Abs(YawRate) < YawAcceleration * dt)
            { yaw = Mathf.Repeat(targetYaw, 360f); YawRate = 0; }

            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            float fuelPower = game.Fuel > 0 ? 1f : .3f;
            Vector3 desired = heading * new Vector3(RequestedMovement.x * StrafeCruise, 0,
                RequestedMovement.y * (RequestedMovement.y >= 0 ? ForwardCruise : ReverseCruise)) * fuelPower;
            Vector3 previousPosition = transform.position;
            Vector3 planar = new Vector3(Velocity.x, 0, Velocity.z);
            Vector3 previousVelocity = Velocity;
            bool braking = RequestedMovement.sqrMagnitude < .001f;
            Vector3 velocityError = desired - planar;
            Vector3 wantedThrust = Vector3.ClampMagnitude(velocityError / dt, braking ? ReleaseBraking : ThrustAcceleration * fuelPower);
            thrust = Vector3.MoveTowards(thrust, wantedThrust, (braking ? BrakeJerk : ThrustJerk) * dt);
            Vector3 changed = planar + thrust * dt;
            if (Vector3.Dot(velocityError, desired - changed) <= 0)
            {
                // Stop at the commanded velocity instead of oscillating around a hover or cruise speed.
                changed = desired;
                thrust = Vector3.zero;
            }
            planar = changed;
            Vector3 next = previousPosition + planar * dt;
            float boundary = World.Radius - 12f;
            if (Mathf.Abs(next.x) > boundary) { next.x = Mathf.Clamp(next.x, -boundary, boundary); planar.x = thrust.x = 0; }
            if (Mathf.Abs(next.z) > boundary) { next.z = Mathf.Clamp(next.z, -boundary, boundary); planar.z = thrust.z = 0; }
            float ground = SafeGround(next.x, next.z);
            holdHeight += ClimbCommand * (ClimbCommand >= 0 ? ClimbSpeed : DescendSpeed) * dt;
            holdHeight = Mathf.Clamp(holdHeight, ground + MinimumClearance, ground + MaximumAltitude);
            // Raise the absolute height hold before a ridge arrives; never follow every contour down.
            float safeFloor = ground + MinimumClearance;
            for (int sample = 1; sample <= 4; sample++)
            {
                Vector3 ahead = next + planar * (sample * .42f);
                safeFloor = Mathf.Max(safeFloor, SafeGround(ahead.x, ahead.z) + MinimumClearance);
            }
            holdHeight = Mathf.Max(holdHeight, safeFloor);
            if (game.Fuel <= 0) holdHeight = Mathf.Max(ground + MinimumClearance, holdHeight - dt * 2f);
            float desiredVertical = Mathf.Clamp((holdHeight - next.y) * 3.5f, -DescendSpeed, ClimbSpeed + 4f);
            verticalSpeed = Mathf.MoveTowards(verticalSpeed, desiredVertical, 14f * dt);
            next.y += verticalSpeed * dt;
            if ((holdHeight - previousPosition.y) * (holdHeight - next.y) <= 0 && Mathf.Abs(ClimbCommand) < .001f)
            { next.y = holdHeight; verticalSpeed = 0; }
            next.y = Mathf.Max(next.y, ground + MinimumClearance); // Safety takes precedence over visual inertia.
            transform.position = next;
            // Keep horizontal momentum as simulation state. Reconstructing it from transform
            // differences amplifies float rounding into false acceleration far from world origin.
            Velocity = new Vector3(planar.x, (next.y - previousPosition.y) / Mathf.Max(.0001f, dt), planar.z);
            Acceleration = (Velocity - previousVelocity) / Mathf.Max(.0001f, dt);
            Vector3 localAcceleration = Quaternion.Inverse(heading) * Acceleration;
            Vector3 localVelocity = Quaternion.Inverse(heading) * Velocity;
            float speedRatio = Mathf.Clamp01(PlanarSpeed / ForwardCruise);
            float desiredPitch = Mathf.Clamp(localAcceleration.z * .62f + localVelocity.z / ForwardCruise * 3.2f, -10f, 11f);
            float desiredBank = Mathf.Clamp(-localAcceleration.x * .75f - localVelocity.x / StrafeCruise * 3f - YawRate / MaximumYawRate * speedRatio * 5f, -14f, 14f);
            float response = 1f - Mathf.Exp(-dt * 5f);
            VisualPitch = Mathf.Lerp(VisualPitch, desiredPitch, response);
            Bank = Mathf.Lerp(Bank, desiredBank, response);
            float load = .18f + RequestedMovement.magnitude * .35f + Mathf.Abs(ClimbCommand) * .25f +
                         Mathf.Clamp01(new Vector2(Acceleration.x, Acceleration.z).magnitude / ThrustAcceleration) * .22f;
            EngineLoad = Mathf.Lerp(EngineLoad, Mathf.Clamp01(load), response);
        }

        // Geometry is a triangulated approximation of the analytic hills. Clear both surfaces.
        static float SafeGround(float x, float z) => Mathf.Max(World.Height(x, z), World.SurfaceHeight(x, z));

        void ApplyView(float dt, bool snap)
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (!Camera) return;
            float motion = Mathf.Clamp01(CameraMotion);
            float response = snap ? 1f : 1f - Mathf.Exp(-dt * 7f);
            Vector3 localAcceleration = Quaternion.Euler(0, -yaw, 0) * Acceleration;
            Vector3 offset = new Vector3(-localAcceleration.x * .006f, -Acceleration.y * .004f, -localAcceleration.z * .006f);
            offset = Vector3.ClampMagnitude(offset, .095f) * motion;
            cameraOffset = Vector3.Lerp(cameraOffset, offset, response);
            cameraPitch = Mathf.Lerp(cameraPitch, VisualPitch * .20f * motion, response);
            cameraRoll = Mathf.Lerp(cameraRoll, Bank * .30f * motion, response);
            if (motion <= 0) { cameraOffset = Vector3.zero; cameraPitch = cameraRoll = 0; }
            Camera.transform.localPosition = cameraOffset;
            Camera.transform.localRotation = Quaternion.Euler(pitch + cameraPitch, 0, cameraRoll);
            float fov = 78f + Mathf.Clamp01(PlanarSpeed / ForwardCruise) * 3f * motion;
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, fov, snap ? 1f : 1f - Mathf.Exp(-dt * 3f));
            if (motion <= 0) Camera.fieldOfView = 78f;
            // The lamp and weapon solution share the stable sight, not the spring-mounted pilot eye.
            if (searchlight) { searchlight.transform.position = AimOrigin; searchlight.transform.rotation = AimRotation; }
            Vector3 p = AimOrigin;
            Shader.SetGlobalVector("_HolyLampPosition", new Vector4(p.x, p.y, p.z, 1));
            Shader.SetGlobalVector("_HolyLampDirection", AimForward);
        }

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            holdHeight = position.y;
            Velocity = Vector3.zero;
            Acceleration = thrust = cameraOffset = Vector3.zero;
            RequestedMovement = Vector2.zero;
            ClimbCommand = YawRate = cameraPitch = cameraRoll = Bank = VisualPitch = 0;
            targetYaw = yaw;
            verticalSpeed = 0;
            ApplyView(0, true);
        }

        public void Teleport(Vector3 position, float heading, float lookPitch)
        {
            SetAim(heading, lookPitch);
            Teleport(position);
        }

        /// <summary>Initial/test pose only; ordinary turning must go through TickCommand.</summary>
        public void SetAim(float heading, float lookPitch)
        {
            yaw = targetYaw = Mathf.Repeat(heading, 360f);
            pitch = Mathf.Clamp(lookPitch, -28f, 78f);
            YawRate = 0;
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
