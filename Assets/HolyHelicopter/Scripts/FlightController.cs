using UnityEngine;
using UnityEngine.InputSystem;

namespace HolyHelicopter
{
    /// <summary>Heading, flight, and camera pitch are deliberately independent.</summary>
    public sealed class FlightController : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Altitude => transform.position.y - World.Height(transform.position.x, transform.position.z);
        public float Heading => yaw;
        public float LookPitch => pitch;
        public float Bank { get; private set; }
        public float VisualPitch { get; private set; }
        public float Sensitivity = .085f;
        public bool InvertY;
        float yaw = -22f, pitch = 19f, verticalSpeed, holdHeight;
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
            Camera.farClipPlane = 1100f;
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
            ApplyView();
        }

        public void Tick(float dt, bool acceptInput)
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 movement = Vector2.zero;
            float climb = 0f;
            if (acceptInput && keys != null)
            {
                movement.x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                movement.y = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                climb = (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0);
                if (mouse != null)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * Sensitivity;
                    pitch = Mathf.Clamp(pitch + delta.y * Sensitivity * (InvertY ? 1f : -1f), -28f, 78f);
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > .01f) holdHeight += Mathf.Sign(scroll) * 3.5f;
                }
            }
            movement = Vector2.ClampMagnitude(movement, 1f);
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            float fuelPower = game.Fuel > 0 ? 1f : .3f;
            Vector3 desired = heading * new Vector3(movement.x, 0, movement.y) * (30f * fuelPower);
            float response = movement.sqrMagnitude > .01f ? 3.5f : 4.8f;
            Vector3 previousPosition = transform.position;
            Vector3 planar = new Vector3(Velocity.x, 0, Velocity.z);
            planar = Vector3.Lerp(planar, desired, 1f - Mathf.Exp(-dt * response));
            Vector3 next = previousPosition + planar * dt;
            next.x = Mathf.Clamp(next.x, -World.Radius + 12, World.Radius - 12);
            next.z = Mathf.Clamp(next.z, -World.Radius + 12, World.Radius - 12);
            float ground = World.Height(next.x, next.z);
            holdHeight += climb * 15f * dt;
            holdHeight = Mathf.Clamp(holdHeight, ground + 3.4f, ground + 58f);
            // Look ahead along actual velocity; only raise the altitude hold, never terrain-follow downward.
            Vector3 lookahead = next + planar * 1.1f;
            float safeFloor = Mathf.Max(ground, World.Height(lookahead.x, lookahead.z));
            Vector3 halfway = (next + lookahead) * .5f;
            safeFloor = Mathf.Max(safeFloor, World.Height(halfway.x, halfway.z)) + 3.4f;
            if (holdHeight < safeFloor) holdHeight = safeFloor;
            if (game.Fuel <= 0) holdHeight = Mathf.Max(ground + 3.4f, holdHeight - dt * 2f);
            next.y = Mathf.SmoothDamp(next.y, holdHeight, ref verticalSpeed, .28f, 22f, dt);
            next.y = Mathf.Max(next.y, ground + 3.4f); // Absolute anti-crash guard even at a steep slope.
            transform.position = next;
            Velocity = (next - previousPosition) / Mathf.Max(.0001f, dt);
            Bank = Mathf.Lerp(Bank, -movement.x * 10f, 1f - Mathf.Exp(-dt * 4f));
            VisualPitch = Mathf.Lerp(VisualPitch, movement.y * 6f, 1f - Mathf.Exp(-dt * 4f));
            ApplyView();
            body.localRotation = Quaternion.Euler(VisualPitch, 0, Bank);
            rotor.Rotate(Vector3.up, dt * 1800f, Space.Self);
        }

        void ApplyView()
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            Vector3 p = Camera.transform.position;
            Shader.SetGlobalVector("_HolyLampPosition", new Vector4(p.x, p.y, p.z, 1));
            Shader.SetGlobalVector("_HolyLampDirection", Camera.transform.forward);
        }

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            holdHeight = position.y;
            Velocity = Vector3.zero;
            verticalSpeed = 0;
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
