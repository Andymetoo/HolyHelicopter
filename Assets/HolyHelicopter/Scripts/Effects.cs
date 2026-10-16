using UnityEngine;
using UnityEngine.Rendering;

namespace HolyHelicopter
{
    /// <summary>Fixed-budget effect pool. A 300-kill rocket never creates 300 particle systems.</summary>
    public sealed class Effects : MonoBehaviour
    {
        const int Capacity = 2400;
        struct Spark { public Vector3 Position, Velocity; public Color Color; public float Life, Total, Size; }
        readonly Spark[] sparks = new Spark[Capacity];
        readonly Vector3[] vertices = new Vector3[Capacity * 6];
        readonly Color[] colors = new Color[Capacity * 6];
        readonly int[] indices = new int[Capacity * 24];
        static readonly Vector3[] Shape = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        static readonly int[] Faces = { 0,4,3, 0,3,5, 0,5,2, 0,2,4, 1,3,4, 1,5,3, 1,2,5, 1,4,2 };
        readonly LineRenderer[] beams = new LineRenderer[100];
        readonly float[] beamEnd = new float[100];
        int cursor, beamCursor;
        Mesh mesh;
        Material material;
        Game game;

        public void Initialize(Game owner)
        {
            game = owner;
            mesh = new Mesh { name = "Batched consecrated debris", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            material = new Material(Resources.Load<Shader>("HolyEffects"));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            for (int p = 0; p < Capacity; p++)
                for (int n = 0; n < Faces.Length; n++) indices[p * 24 + n] = p * 6 + Faces[n];
            var lineMaterial = World.Mat("Tracer light", Color.white, true);
            for (int i = 0; i < beams.Length; i++)
            {
                var go = new GameObject("Pooled tracer " + i);
                go.transform.SetParent(transform);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = lineMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                beams[i] = line;
            }
        }

        public void Burst(Vector3 position, Color color, int count, float size = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                float life = Random.Range(.22f, .65f);
                sparks[cursor] = new Spark
                {
                    Position = position + Random.insideUnitSphere * .55f,
                    Velocity = (Random.insideUnitSphere * 8f + Vector3.up * 6f) * Mathf.Sqrt(size),
                    Color = color, Life = life, Total = life, Size = Random.Range(.08f, .25f) * size
                };
                cursor = (cursor + 1) % Capacity;
            }
        }

        public void Beam(Vector3 from, Vector3 to, Color color, float width, float life)
        {
            var line = beams[beamCursor];
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = width;
            line.endWidth = width * .55f;
            line.startColor = color;
            line.endColor = color * new Color(1, 1, 1, .65f);
            line.enabled = true;
            beamEnd[beamCursor] = Time.time + life;
            beamCursor = (beamCursor + 1) % beams.Length;
        }

        public void Ring(Vector3 center, float radius, Color color, float life = .28f)
        {
            const int sides = 24;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                Vector3 q = center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius;
                p.y = World.Height(p.x, p.z) + .5f;
                q.y = World.Height(q.x, q.z) + .5f;
                Beam(p, q, color, .2f, life);
            }
        }

        void LateUpdate()
        {
            if (material == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            float dt = game.Paused ? 0 : Mathf.Min(Time.deltaTime, .05f);
            int batch = 0;
            Vector3 cameraPosition = game.Flight.Camera.transform.position;
            for (int i = 0; i < Capacity; i++)
            {
                if (sparks[i].Life <= 0) continue;
                var s = sparks[i];
                s.Life -= dt;
                s.Velocity.y -= dt * 14f;
                s.Position += s.Velocity * dt;
                sparks[i] = s;
                if (s.Life <= 0) continue;
                // Debris entering the canopy shrinks away instead of covering the pilot's view.
                float nearFade = Mathf.InverseLerp(2.25f, 16f, (s.Position - cameraPosition).sqrMagnitude);
                if (nearFade <= .01f) continue;
                for (int n = 0; n < 6; n++)
                {
                    vertices[batch * 6 + n] = s.Position + Shape[n] * (s.Size * s.Life / s.Total * nearFade);
                    colors[batch * 6 + n] = s.Color;
                }
                batch++;
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices, 0, batch * 6);
            mesh.SetColors(colors, 0, batch * 6);
            mesh.SetIndices(indices, 0, batch * 24, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2200f);
            for (int i = 0; i < beams.Length; i++)
                if (beams[i].enabled && Time.time >= beamEnd[i]) beams[i].enabled = false;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (mesh) Destroy(mesh);
        }
    }
}
