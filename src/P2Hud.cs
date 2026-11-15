using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Player 2's HUD, built from copies of the game's own HUD pieces: a mirrored soul
    /// vessel in the top-right with masks running toward the middle. Also moves the
    /// (shared) geo counter to the top middle.
    /// </summary>
    internal class P2Hud
    {
        Transform canvas;
        Camera hudCam;
        GameObject maskTemplate;
        Vector3 mask1Pos;
        float maskDx = 0.9f;
        readonly List<GameObject> masks = new List<GameObject>();
        readonly Dictionary<tk2dSprite, Color> originalColors = new Dictionary<tk2dSprite, Color>();
        // (game piece, our copy) pairs: copies follow the game's look each frame.
        readonly List<KeyValuePair<Transform, Transform>> orbPairs = new List<KeyValuePair<Transform, Transform>>();
        readonly List<List<KeyValuePair<Transform, Transform>>> maskPairs = new List<List<KeyValuePair<Transform, Transform>>>();
        GameObject orb;
        Transform orbSource;
        Transform geo;
        Vector3 geoOriginal;
        bool geoMoved;
        float geoTimer;
        float retry;
        bool loggedTree;

        public bool Ready { get; private set; }

        int lastMaskCount = -1;
        readonly List<float> maskAnimTimers = new List<float>();
        string breakClip, refillClip;
        bool clipsChecked;

        public void Update(int maskCount, int maxMasks, Color tint, bool visible, float dt)
        {
            if (!Ready)
            {
                retry -= dt;
                if (retry > 0f) return;
                retry = 2f;
                try { Build(); }
                catch (Exception e)
                {
                    LocalCoop.Info("Player 2 HUD: couldn't build (" + e.Message + "), using simple HUD.");
                    Clear();
                }
                if (!Ready) return;
            }

            if (canvas == null || maskTemplate == null)
            {
                Clear();
                return;
            }

            while (masks.Count < maxMasks) masks.Add(MakeMask(masks.Count));
            while (maskAnimTimers.Count < masks.Count) maskAnimTimers.Add(0f);
            for (int i = 0; i < maskAnimTimers.Count; i++) maskAnimTimers[i] -= dt;

            // Break / refill animations when Player 2 loses or gains masks.
            if (lastMaskCount >= 0 && maskCount != lastMaskCount)
            {
                CheckClips();
                if (maskCount < lastMaskCount)
                    for (int i = maskCount; i < lastMaskCount && i < masks.Count; i++) PlayMask(i, breakClip);
                else
                    for (int i = lastMaskCount; i < maskCount && i < masks.Count; i++) PlayMask(i, refillClip);
            }
            lastMaskCount = maskCount;
            // Multipliers: keep each piece's own colour and transparency, just tint it.
            Color full = new Color(1f, 1f, 1f, 1f); // same as Player 1's
            Color empty = new Color(0.22f, 0.22f, 0.26f, 1f);
            if (visible)
            {
                MirrorPairs(orbPairs);
                foreach (var pairs in maskPairs) MirrorPairs(pairs);
            }
            for (int i = 0; i < masks.Count; i++)
            {
                if (masks[i] == null) continue;
                bool show = visible && i < maxMasks;
                if (masks[i].activeSelf != show) masks[i].SetActive(show);
                // While a break animation plays, don't darken it yet.
                if (show) SetColor(masks[i], (i < maskCount || maskAnimTimers[i] > 0f) ? full : empty);
            }
            if (orb != null)
            {
                if (orb.activeSelf != visible) orb.SetActive(visible);
            }

            geoTimer -= dt;
            if (geoTimer <= 0f)
            {
                geoTimer = 0.5f;
                CenterGeo();
            }
        }

        /// <summary>Screen rectangle (GUI coordinates) just under Player 2's masks, for the soul bar.</summary>
        public bool SoulBarRect(int maxMasks, out Rect rect)
        {
            rect = new Rect(0f, 0f, 0f, 0f);
            if (!Ready || hudCam == null) return false;
            float cx = CenterX();
            if (maskTemplate != null) mask1Pos = maskTemplate.transform.position;
            Vector3 first = new Vector3(2f * cx - mask1Pos.x, mask1Pos.y, mask1Pos.z);
            Vector3 last = new Vector3(2f * cx - (mask1Pos.x + (maxMasks - 1) * maskDx), mask1Pos.y, mask1Pos.z);
            Vector3 a = hudCam.WorldToScreenPoint(first);
            Vector3 b = hudCam.WorldToScreenPoint(last);
            float left = Mathf.Min(a.x, b.x) - 12f;
            float right = Mathf.Max(a.x, b.x) + 12f;
            float y = Screen.height - a.y + 34f;
            rect = new Rect(left, y, right - left, 6f);
            return true;
        }

        void CheckClips()
        {
            if (clipsChecked || maskTemplate == null) return;
            clipsChecked = true;
            var names = new List<string>();
            foreach (tk2dSpriteAnimator an in maskTemplate.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                var clips = Game.Get(an.Library, "clips") as Array;
                if (clips == null) continue;
                foreach (object c in clips)
                {
                    string n = Game.Get(c, "name") as string;
                    if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
                }
            }
            foreach (string n in names)
            {
                string l = n.ToLower();
                if (breakClip == null && (l.Contains("break") || l.Contains("hurt") || l.Contains("damage"))) breakClip = n;
                if (refillClip == null && (l.Contains("refill") || l.Contains("heal") || l.Contains("fill") || l.Contains("appear"))) refillClip = n;
            }
            LocalCoop.Info("Mask animations: " + string.Join(", ", names.ToArray()) +
                           " -> break '" + breakClip + "', refill '" + refillClip + "'");
        }

        void PlayMask(int i, string clip)
        {
            if (string.IsNullOrEmpty(clip) || masks[i] == null) return;
            foreach (tk2dSpriteAnimator an in masks[i].GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                if (an != null && an.GetClipByName(clip) != null)
                {
                    an.Play(clip);
                    maskAnimTimers[i] = 0.6f;
                }
            }
        }

        public void Clear()
        {
            foreach (GameObject m in masks)
                if (m != null) UnityEngine.Object.Destroy(m);
            masks.Clear();
            maskPairs.Clear();
            orbPairs.Clear();
            originalColors.Clear();
            maskAnimTimers.Clear();
            lastMaskCount = -1;
            if (orb != null) UnityEngine.Object.Destroy(orb);
            orb = null;
            if (geo != null && geoMoved) geo.position = geoOriginal;
            geoMoved = false;
            Ready = false;
        }

        // ------------------------------------------------------------ building

        void Build()
        {
            object gc = Game.StaticValue("GameCameras", "_instance", "instance");
            var canvasGo = Game.Get(gc, "hudCanvas") as GameObject;
            hudCam = Game.Get(gc, "hudCamera") as Camera;
            if (canvasGo == null) return;
            canvas = canvasGo.transform;

            if (!loggedTree)
            {
                loggedTree = true;
                LocalCoop.Info("HUD layout: " + Describe(canvas, 0));
            }

            Transform m1 = FindDeep(canvas, "Health 1");
            Transform m2 = FindDeep(canvas, "Health 2");
            Transform orbSrc = FindDeepPrefix(canvas, "Soul Orb");
            geo = FindDeep(canvas, "Geo Counter");
            if (m1 == null)
            {
                LocalCoop.Info("Player 2 HUD: no mask to copy, using simple HUD.");
                return;
            }

            maskTemplate = m1.gameObject;
            mask1Pos = m1.position;
            if (m2 != null && Mathf.Abs(m2.position.x - m1.position.x) > 0.01f) maskDx = m2.position.x - m1.position.x;

            if (orbSrc != null)
            {
                orbSource = orbSrc;
                orb = Clone(orbSrc.gameObject, orbPairs);
                // Don't copy masks/geo if they live inside the vessel.
                RemoveChildren(orb.transform, "Health");
                RemoveChildren(orb.transform, "Geo");
                Vector3 s = orb.transform.localScale;
                orb.transform.localScale = new Vector3(-s.x, s.y, s.z);
                orb.transform.position = Mirror(orbSrc.position);
            }

            Ready = true;
            LocalCoop.Info("Player 2 HUD: built (vessel " + (orb != null ? "yes" : "no") +
                           ", geo counter " + (geo != null ? "yes" : "no") + ").");
        }

        GameObject MakeMask(int index)
        {
            var pairs = new List<KeyValuePair<Transform, Transform>>();
            GameObject m = Clone(maskTemplate, pairs);
            maskPairs.Add(pairs);
            Vector3 t = maskTemplate.transform.position; // live: the HUD slides in and out
            Vector3 p = new Vector3(t.x + index * maskDx, t.y, t.z);
            m.transform.position = Mirror(p);
            return m;
        }

        /// <summary>A copy with the game's scripts removed, so it only shows what we tell it.</summary>
        GameObject Clone(GameObject src, List<KeyValuePair<Transform, Transform>> pairs)
        {
            var holder = new GameObject("LocalCoop HUD holder");
            holder.SetActive(false);
            GameObject c = UnityEngine.Object.Instantiate(src, holder.transform);
            if (pairs != null) Pair(src.transform, c.transform, pairs, true);
            foreach (MonoBehaviour mb in c.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n.StartsWith("tk2d") || n.StartsWith("TextMesh") || n.StartsWith("TMP")) continue;
                UnityEngine.Object.DestroyImmediate(mb);
            }
            c.name = "LocalCoop P2 " + src.name;
            c.transform.parent = src.transform.parent;
            c.transform.localPosition = src.transform.localPosition;
            c.transform.localRotation = src.transform.localRotation;
            c.transform.localScale = src.transform.localScale;
            UnityEngine.Object.Destroy(holder);
            c.SetActive(true);
            // Remember the copy's colours as the game left them (transparent parts stay transparent).
            foreach (tk2dSprite sp in c.GetComponentsInChildren<tk2dSprite>(true))
                if (sp != null && !originalColors.ContainsKey(sp)) originalColors[sp] = sp.color;
            return c;
        }

        float CenterX()
        {
            if (hudCam != null) return hudCam.transform.position.x;
            return canvas != null ? canvas.position.x : 0f;
        }

        Vector3 Mirror(Vector3 p)
        {
            return new Vector3(2f * CenterX() - p.x, p.y, p.z);
        }

        void CenterGeo()
        {
            // Left in its normal place for now; log where things are so it can be moved safely.
            if (geo == null || maskTemplate == null || geoMoved) return;
            geoMoved = true;
            geoOriginal = geo.position;
            Renderer[] rs = geo.GetComponentsInChildren<Renderer>();
            var parts = new List<string>();
            foreach (Renderer r in rs)
                if (r != null) parts.Add(r.gameObject.name + (r.enabled ? "" : "(off)") + " " + r.bounds.center + "/" + r.bounds.size);
            LocalCoop.Info("Geo counter at " + geo.position + " (parent " + (geo.parent != null ? geo.parent.name : "-") +
                           "), masks at " + maskTemplate.transform.position + ", HUD camera x " + CenterX() +
                           "; parts: " + string.Join("; ", parts.ToArray()));
        }

        void Pair(Transform src, Transform copy, List<KeyValuePair<Transform, Transform>> pairs, bool root)
        {
            if (!root) pairs.Add(new KeyValuePair<Transform, Transform>(src, copy));
            int n = Mathf.Min(src.childCount, copy.childCount);
            for (int i = 0; i < n; i++) Pair(src.GetChild(i), copy.GetChild(i), pairs, false);
        }

        /// <summary>Copy shown/hidden state, frame and colour from the game's piece to ours.</summary>
        void MirrorPairs(List<KeyValuePair<Transform, Transform>> pairs)
        {
            foreach (var kv in pairs)
            {
                Transform src = kv.Key, dst = kv.Value;
                if (src == null || dst == null) continue;
                if (dst.gameObject.activeSelf != src.gameObject.activeSelf) dst.gameObject.SetActive(src.gameObject.activeSelf);
                if (!src.gameObject.activeSelf) continue;
                dst.localPosition = src.localPosition;
                dst.localScale = src.localScale;
                dst.localRotation = src.localRotation;
                var sr = src.GetComponent<Renderer>();
                var dr = dst.GetComponent<Renderer>();
                if (sr != null && dr != null) dr.enabled = sr.enabled;
                var ss = src.GetComponent<tk2dSprite>();
                var ds = dst.GetComponent<tk2dSprite>();
                if (ss != null && ds != null)
                {
                    if (ds.spriteId != ss.spriteId) ds.spriteId = ss.spriteId;
                    originalColors[ds] = ss.color;
                    ds.color = ss.color;
                }
            }
        }

        void SetColor(GameObject go, Color mul)
        {
            foreach (tk2dSprite s in go.GetComponentsInChildren<tk2dSprite>(true))
            {
                if (s == null) continue;
                Color o;
                if (!originalColors.TryGetValue(s, out o)) { o = s.color; originalColors[s] = o; }
                s.color = new Color(o.r * mul.r, o.g * mul.g, o.b * mul.b, o.a * mul.a);
            }
        }

        static void RemoveChildren(Transform t, string prefix)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform c = t.GetChild(i);
                if (c.name.StartsWith(prefix)) UnityEngine.Object.Destroy(c.gameObject);
                else RemoveChildren(c, prefix);
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Transform FindDeepPrefix(Transform t, string prefix)
        {
            if (t.name.StartsWith(prefix)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeepPrefix(t.GetChild(i), prefix);
                if (r != null) return r;
            }
            return null;
        }

        static string Describe(Transform t, int depth)
        {
            if (depth > 2) return "";
            var parts = new List<string>();
            for (int i = 0; i < t.childCount && i < 40; i++)
            {
                Transform c = t.GetChild(i);
                string inner = Describe(c, depth + 1);
                parts.Add(c.name + (inner.Length > 0 ? " [" + inner + "]" : ""));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
