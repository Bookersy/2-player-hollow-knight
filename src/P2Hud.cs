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
        GameObject orb;
        Transform orbSource;
        Transform geo;
        Vector3 geoOriginal;
        bool geoMoved;
        float geoTimer;
        float retry;
        bool loggedTree;

        public bool Ready { get; private set; }

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
            // Multipliers: keep each piece's own colour and transparency, just tint it.
            Color full = new Color(Mathf.Lerp(1f, tint.r, 0.35f), Mathf.Lerp(1f, tint.g, 0.35f), Mathf.Lerp(1f, tint.b, 0.35f), 1f);
            Color empty = new Color(0.18f, 0.18f, 0.24f, 1f);
            for (int i = 0; i < masks.Count; i++)
            {
                if (masks[i] == null) continue;
                bool show = visible && i < maxMasks;
                if (masks[i].activeSelf != show) masks[i].SetActive(show);
                if (show) SetColor(masks[i], i < maskCount ? full : empty);
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

        public void Clear()
        {
            foreach (GameObject m in masks)
                if (m != null) UnityEngine.Object.Destroy(m);
            masks.Clear();
            originalColors.Clear();
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
                orb = Clone(orbSrc.gameObject);
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
            GameObject m = Clone(maskTemplate);
            Vector3 t = maskTemplate.transform.position; // live: the HUD slides in and out
            Vector3 p = new Vector3(t.x + index * maskDx, t.y, t.z);
            m.transform.position = Mirror(p);
            return m;
        }

        /// <summary>A copy with the game's scripts removed, so it only shows what we tell it.</summary>
        GameObject Clone(GameObject src)
        {
            var holder = new GameObject("LocalCoop HUD holder");
            holder.SetActive(false);
            GameObject c = UnityEngine.Object.Instantiate(src, holder.transform);
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
            if (geo == null || maskTemplate == null || hudCam == null) return;

            if (!geoMoved)
            {
                geoOriginal = geo.position;
                geoMoved = true;
                LocalCoop.Info("Geo counter was at " + geo.position + ", masks at " + maskTemplate.transform.position);
            }

            // Measure only the geo counter's own small pieces (icon + digits).
            float minX = 0f, maxX = 0f;
            bool any = false;
            foreach (Renderer r in geo.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                if (b.max.x - b.min.x > 6f) continue; // ignore big glows/backgrounds
                if (!any) { minX = b.min.x; maxX = b.max.x; any = true; }
                else { minX = Mathf.Min(minX, b.min.x); maxX = Mathf.Max(maxX, b.max.x); }
            }
            float width = any ? Mathf.Max(0.5f, maxX - minX) : 2f;
            float leftOffset = any ? geo.position.x - minX : 0f;

            // Left edge so the whole counter is centred; same height as the mask row.
            float targetX = CenterX() - width * 0.5f + leftOffset;
            float targetY = maskTemplate.transform.position.y;
            Vector3 target = new Vector3(targetX, targetY, geo.position.z);

            // Never put it somewhere you can't see it.
            Vector3 vp = hudCam.WorldToViewportPoint(target);
            if (vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.98f)
            {
                geo.position = geoOriginal;
                return;
            }
            geo.position = target;
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
