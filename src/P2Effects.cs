using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Player 2's sounds, dust and "game feel", borrowed from the Knight: the same audio
    /// clips (from his HeroAudioController and nail slashes), the same effect prefabs
    /// (landing dust, jump puff, run dust, wall-slide dust, dash), plus camera shake and a
    /// tiny freeze when Player 2 is hit.
    /// </summary>
    internal class P2Effects
    {
        static readonly string[] SoundFields =
        {
            "softLanding", "hardLanding", "jump", "takeHit", "backDash", "dash",
            "footStepsRun", "footStepsWalk", "wallslide", "walljump", "falling"
        };

        static readonly string[] PrefabFields =
        {
            "softLandingEffectPrefab", "hardLandingEffectPrefab", "runEffectPrefab", "jumpEffectPrefab",
            "jumpTrailPrefab", "wallslideDustPrefab", "dashParticlesPrefab", "backDashPrefab"
        };

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        HeroController hero;
        GameObject owner;
        AudioSource oneShot, runLoop, slideLoop;
        GameObject dashBurstSource;
        GameObject slideDust;

        public void Init(HeroController h, GameObject p2)
        {
            hero = h;
            owner = p2;
            try
            {
                Component hac = hero.GetComponent("HeroAudioController");
                UnityEngine.Audio.AudioMixerGroup mixer = null;
                foreach (string f in SoundFields)
                {
                    var src = Game.Get(hac, f) as AudioSource;
                    if (src == null || src.clip == null) continue;
                    clips[f] = src.clip;
                    if (mixer == null) mixer = src.outputAudioMixerGroup;
                }

                foreach (string n in new[] { "Slash", "AltSlash", "UpSlash", "DownSlash" })
                {
                    Transform t = hero.transform.Find("Attacks/" + n);
                    var a = t == null ? null : t.GetComponent<AudioSource>();
                    if (a != null && a.clip != null)
                    {
                        clips["slash" + n] = a.clip;
                        if (mixer == null) mixer = a.outputAudioMixerGroup;
                    }
                }

                foreach (string f in PrefabFields)
                {
                    var go = Game.Get(hero, f) as GameObject;
                    if (go != null) prefabs[f] = go;
                }
                dashBurstSource = FindDeep(hero.transform, "Dash Burst");

                oneShot = MakeSource(mixer, false);
                runLoop = MakeSource(mixer, true);
                slideLoop = MakeSource(mixer, true);

                LocalCoop.Info("Player 2 sounds: " + string.Join(", ", new List<string>(clips.Keys).ToArray()) +
                               " | effects: " + string.Join(", ", new List<string>(prefabs.Keys).ToArray()) +
                               (dashBurstSource != null ? ", Dash Burst" : ""));
            }
            catch (Exception e)
            {
                LocalCoop.Info("Player 2 effects setup error: " + e.Message);
            }
        }

        AudioSource MakeSource(UnityEngine.Audio.AudioMixerGroup mixer, bool loop)
        {
            AudioSource a = owner.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f;
            if (mixer != null) a.outputAudioMixerGroup = mixer; // follows the game's SFX volume
            return a;
        }

        // ------------------------------------------------------------ sounds

        public void Sound(string name, float volume = 1f)
        {
            AudioClip c;
            if (oneShot != null && clips.TryGetValue(name, out c)) oneShot.PlayOneShot(c, volume);
        }

        public void SetRunning(bool on)
        {
            Loop(runLoop, "footStepsRun", on);
        }

        public void SetWallSliding(bool on, bool facingRight)
        {
            Loop(slideLoop, "wallslide", on);
            if (on && slideDust == null)
            {
                slideDust = Spawn("wallslideDustPrefab", owner.transform.position, facingRight, 0f);
                if (slideDust != null) slideDust.transform.parent = owner.transform;
            }
            else if (!on && slideDust != null)
            {
                UnityEngine.Object.Destroy(slideDust);
                slideDust = null;
            }
        }

        void Loop(AudioSource src, string clip, bool on)
        {
            AudioClip c;
            if (src == null || !clips.TryGetValue(clip, out c)) return;
            if (on && !src.isPlaying)
            {
                src.clip = c;
                src.Play();
            }
            else if (!on && src.isPlaying)
            {
                src.Stop();
            }
        }

        public void StopLoops()
        {
            if (runLoop != null && runLoop.isPlaying) runLoop.Stop();
            SetWallSliding(false, true);
        }

        // ------------------------------------------------------------ dust and effects

        /// <summary>
        /// Spawns a visual-only copy of one of the Knight's effect prefabs (its own scripts,
        /// which follow the Knight, are removed) and plays its animations/particles.
        /// </summary>
        public GameObject Spawn(string prefabField, Vector3 pos, bool facingRight, float lifetime = 2f)
        {
            GameObject prefab;
            if (!prefabs.TryGetValue(prefabField, out prefab)) return null;
            return SpawnCopy(prefab, pos, facingRight, lifetime);
        }

        public void DashBurst(Vector3 pos, bool facingRight)
        {
            Spawn("dashParticlesPrefab", pos, facingRight);
            if (dashBurstSource != null) SpawnCopy(dashBurstSource, pos, facingRight, 1f);
        }

        GameObject SpawnCopy(GameObject prefab, Vector3 pos, bool facingRight, float lifetime)
        {
            try
            {
                var holder = new GameObject("LocalCoop fx holder");
                holder.SetActive(false);
                GameObject fx = UnityEngine.Object.Instantiate(prefab, holder.transform);
                foreach (MonoBehaviour mb in fx.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name.StartsWith("tk2d")) continue;
                    UnityEngine.Object.DestroyImmediate(mb);
                }
                fx.transform.parent = null;
                fx.transform.position = new Vector3(pos.x, pos.y, prefab.transform.position.z);
                Vector3 s = prefab.transform.localScale;
                // The Knight's effects face left by default, like his sprite.
                fx.transform.localScale = new Vector3(facingRight ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
                UnityEngine.Object.Destroy(holder);
                fx.SetActive(true);

                foreach (tk2dSpriteAnimator an in fx.GetComponentsInChildren<tk2dSpriteAnimator>(true))
                    if (an != null && an.gameObject.activeInHierarchy) an.Play();
                foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps != null) ps.Play();

                if (lifetime > 0f) UnityEngine.Object.Destroy(fx, lifetime);
                return fx;
            }
            catch (Exception e)
            {
                LocalCoop.Info("Player 2 effect error (" + prefab.name + "): " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------ hit feel

        static MethodInfo freezeMethod, sendEventMethod;
        static bool feelChecked;

        public void HitFeel()
        {
            try
            {
                if (!feelChecked)
                {
                    feelChecked = true;
                    GameManager gm0 = Game.GM;
                    if (gm0 != null) freezeMethod = gm0.GetType().GetMethod("FreezeMoment", new[] { typeof(int) });
                }
                GameManager gm = Game.GM;
                if (freezeMethod != null && gm != null) freezeMethod.Invoke(gm, new object[] { 1 });

                object gc = Game.StaticValue("GameCameras", "_instance", "instance");
                object shake = Game.Get(gc, "cameraShakeFSM");
                if (shake != null)
                {
                    if (sendEventMethod == null) sendEventMethod = shake.GetType().GetMethod("SendEvent", new[] { typeof(string) });
                    if (sendEventMethod != null) sendEventMethod.Invoke(shake, new object[] { "AverageShake" });
                }
            }
            catch (Exception e)
            {
                LocalCoop.Info("Player 2 hit feel error: " + e.Message);
            }
        }

        static GameObject FindDeep(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            for (int i = 0; i < t.childCount; i++)
            {
                GameObject r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
