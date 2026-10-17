using UnityEngine;

namespace HolyHelicopter
{
    public enum SoundCue { Minigun, Water, Rocket, Explosion, UV, Shock, Pickup, Hurt, Select, Upgrade, Empty, Rescue, Crypt, Threat }

    public sealed class SynthAudio : MonoBehaviour
    {
        readonly AudioClip[] clips = new AudioClip[14];
        readonly AudioSource[] voices = new AudioSource[20];
        AudioSource rotor, ambience;
        int voice;
        Game game;
        public bool Muted;

        public void Initialize(Game owner)
        {
            game = owner;
            for (int i = 0; i < clips.Length; i++) clips[i] = MakeClip((SoundCue)i);
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].spatialBlend = 0;
                voices[i].playOnAwake = false;
            }
            rotor = gameObject.AddComponent<AudioSource>();
            rotor.clip = Loop(false); rotor.loop = true; rotor.volume = .12f; rotor.Play();
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = Loop(true); ambience.loop = true; ambience.volume = .12f; ambience.Play();
        }

        public void Play(SoundCue cue, float volume = .5f)
        {
            if (Muted) return;
            var source = voices[voice];
            source.pitch = cue == SoundCue.Threat ? 1f : Random.Range(.94f, 1.07f);
            source.clip = clips[(int)cue]; source.volume = volume * .6f; source.Play();
            voice = (voice + 1) % voices.Length;
        }

        void Update()
        {
            if (!rotor || !game.Flight) return;
            float response = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f);
            float load = game.Flight.EngineLoad;
            float level = game.Started && !game.GameOver ? .095f + load * .055f : .055f;
            rotor.volume = Muted ? 0 : Mathf.Lerp(rotor.volume, level, response);
            // Thrust and collective load change the chop before airspeed catches up.
            float pitch = .89f + load * .16f + game.Flight.PlanarSpeed * .0015f;
            rotor.pitch = Mathf.Lerp(rotor.pitch, pitch, response);
            ambience.volume = Muted ? 0 : .105f;
        }

        static AudioClip MakeClip(SoundCue cue)
        {
            const int rate = 22050;
            float duration = cue == SoundCue.Threat ? .36f : cue == SoundCue.Explosion || cue == SoundCue.Crypt || cue == SoundCue.Shock ? .9f : cue == SoundCue.Upgrade || cue == SoundCue.Rescue ? .6f : .15f;
            int length = Mathf.CeilToInt(duration * rate);
            var data = new float[length];
            var rng = new System.Random(771 + (int)cue);
            float smoothNoise = 0;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate, u = t / duration;
                float noise = (float)rng.NextDouble() * 2 - 1;
                smoothNoise = smoothNoise * .88f + noise * .12f;
                float env = Mathf.Pow(1 - u, 2) * Mathf.Min(1, t * 900);
                float s;
                switch (cue)
                {
                    case SoundCue.Minigun: s = noise * .65f + Mathf.Sin(t * (165 - u * 90) * 6.283f) * .35f; break;
                    case SoundCue.Water: s = smoothNoise * 2.3f + Mathf.Sin(t * 670 * 6.283f) * .08f; break;
                    case SoundCue.Rocket: s = noise * .35f + smoothNoise * 1.5f; break;
                    case SoundCue.Explosion: case SoundCue.Crypt: case SoundCue.Shock:
                        s = smoothNoise * 2f + Mathf.Sin(6.283f * (85 * t - 35 * t * t)) * .7f; break;
                    case SoundCue.UV: s = Mathf.Sin(t * 6.283f * 250) * .24f + Mathf.Sin(t * 6.283f * 505) * .12f; break;
                    case SoundCue.Hurt: s = noise * .4f + Mathf.Sin(t * 6.283f * 135) * .4f; break;
                    case SoundCue.Empty: s = noise * Mathf.Exp(-t * 80) * .8f; break;
                    case SoundCue.Threat:
                        float warningFrequency = t < .18f ? 620f : 930f;
                        float pulseTime = t % .18f;
                        env = Mathf.Clamp01(pulseTime / .012f) * Mathf.Clamp01((.15f - pulseTime) / .025f);
                        s = Mathf.Sin(t * warningFrequency * 6.283185f) * .5f + Mathf.Sin(t * warningFrequency * 12.56637f) * .12f;
                        break;
                    default:
                        float frequency = cue == SoundCue.Upgrade || cue == SoundCue.Rescue ? (u < .33f ? 523 : u < .66f ? 659 : 784) : 740 + u * 300;
                        s = Mathf.Sin(t * frequency * 6.283f) * .38f + Mathf.Sin(t * frequency * 12.566f) * .08f;
                        env = Mathf.Sin(u * Mathf.PI) * .7f;
                        break;
                }
                data[i] = Mathf.Clamp(s * env * .7f, -.95f, .95f);
            }
            var clip = AudioClip.Create("Synth / " + cue, length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Loop(bool music)
        {
            const int rate = 22050;
            int length = rate * (music ? 8 : 2);
            var data = new float[length];
            var rng = new System.Random(44);
            float smooth = 0;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                smooth = smooth * .94f + ((float)rng.NextDouble() * 2 - 1) * .06f;
                if (music)
                {
                    float pad = Mathf.Sin(t * 55 * 6.283185f) * .22f + Mathf.Sin(t * 82.5f * 6.283185f) * .12f + Mathf.Sin(t * 65.5f * 6.283185f) * .1f;
                    data[i] = pad * (.65f + .2f * Mathf.Sin(t * .785398f)) + smooth * .16f;
                }
                else
                {
                    float pulse = Mathf.Pow(.5f + .5f * Mathf.Sin(t * 6.283185f * 19), 5);
                    data[i] = (Mathf.Sin(t * 6.283185f * 57) * .25f + smooth * 1.7f) * (.2f + pulse * .8f);
                }
            }
            var clip = AudioClip.Create(music ? "Midnight organ drone" : "Rotor chop", length, 1, rate, false);
            clip.SetData(data, 0); return clip;
        }

        void OnDestroy()
        {
            foreach (var clip in clips) if (clip) Destroy(clip);
            if (rotor && rotor.clip) Destroy(rotor.clip);
            if (ambience && ambience.clip) Destroy(ambience.clip);
        }
    }
}
