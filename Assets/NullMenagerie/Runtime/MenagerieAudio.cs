using UnityEngine;

namespace NullMenagerie
{
    public sealed class MenagerieAudio : MonoBehaviour
    {
        AudioSource source;
        AudioClip hit, special, discover, victory;
        public bool Muted;
        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.volume = .22f;
            hit = Tone("Glyph impact", 90, .09f, 0);
            special = Tone("Data discharge", 260, .28f, 1);
            discover = Tone("Specimen materialization", 420, .48f, 2);
            victory = Tone("Survivor signal", 330, .85f, 3);
        }
        static AudioClip Tone(string name, float hz, float seconds, int kind)
        {
            const int rate = 22050;
            float[] data = new float[(int)(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate, p = t / seconds;
                float freq = hz * (kind == 0 ? 1 - p * .7f : kind == 1 ? 1 + p * 2 : 1 + Mathf.Floor(p * 4) * .25f);
                data[i] = Mathf.Sin(t * freq * Mathf.PI * 2) * Mathf.Pow(1 - p, 2) * Mathf.Min(1, t * 100) * .6f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }
        public void Play(string kind)
        {
            if (Muted || !source) return;
            source.PlayOneShot(kind == "hit" ? hit : kind == "result" ? victory : kind == "discover" ? discover : special);
        }
        void OnDestroy()
        {
            Destroy(hit); Destroy(special); Destroy(discover); Destroy(victory);
        }
    }
}
