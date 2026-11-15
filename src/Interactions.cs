using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Lets Player 2 bring up "press up" prompts (Sit, Listen, Inspect, Ascend...).
    ///
    /// Those prompts are PlayMaker FSMs on trigger areas that wait for the Knight to walk
    /// in, then listen for up. When Player 2 walks into one we tell it the Knight's
    /// collider entered (so the prompt shows), and when Player 2 leaves we tell it he left,
    /// unless the Knight really is standing there.
    /// </summary>
    internal class Interactions
    {
        const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const float Interval = 0.1f;

        static bool initDone;
        static Type fsmType;
        static PropertyInfo statesProp, actionsProp, nameProp;

        readonly Dictionary<Component, bool> promptCache = new Dictionary<Component, bool>();
        readonly List<Collider2D> armed = new List<Collider2D>();
        readonly Dictionary<Collider2D, GameObject> armedFsmObject = new Dictionary<Collider2D, GameObject>();
        readonly HashSet<string> loggedNear = new HashSet<string>();
        float timer;
        bool loggedFirst;

        public bool P2InRange
        {
            get { return armed.Count > 0; }
        }

        public void Update(Vector2 p2Center, HeroController hero, float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = Interval;

            try
            {
                if (!Init()) return;
                Collider2D heroCol = hero.GetComponent<Collider2D>();
                if (heroCol == null) return;
                Vector2 heroPos = heroCol.bounds.center;

                // Leave regions Player 2 has walked out of.
                for (int i = armed.Count - 1; i >= 0; i--)
                {
                    Collider2D col = armed[i];
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    {
                        if (col != null) armedFsmObject.Remove(col);
                        armed.RemoveAt(i);
                        continue;
                    }
                    if (!col.OverlapPoint(p2Center))
                    {
                        if (!col.OverlapPoint(heroPos)) SendBoth(col, "OnTriggerExit2D", heroCol);
                        armedFsmObject.Remove(col);
                        armed.RemoveAt(i);
                    }
                }

                // Enter regions Player 2 has walked into.
                UnityEngine.Object[] fsms = UnityEngine.Object.FindObjectsOfType(fsmType);
                for (int i = 0; i < fsms.Length; i++)
                {
                    var fsm = fsms[i] as Component;
                    if (fsm == null) continue;
                    bool prompt = IsPrompt(fsm);

                    // The trigger area can be on the FSM's object or one of its children.
                    Collider2D[] cols = fsm.GetComponentsInChildren<Collider2D>();
                    for (int c = 0; c < cols.Length; c++)
                    {
                        Collider2D col = cols[c];
                        if (col == null || !col.enabled || !col.isTrigger || armed.Contains(col)) continue;
                        if (!col.OverlapPoint(p2Center)) continue;

                        LogNear(fsm, col, prompt);
                        if (!prompt) continue;

                        armed.Add(col);
                        armedFsmObject[col] = fsm.gameObject;
                        SendBoth(col, "OnTriggerEnter2D", heroCol);
                        if (!loggedFirst)
                        {
                            loggedFirst = true;
                            LocalCoop.Info("Player 2 brought up a prompt on '" + col.gameObject.name + "'.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LocalCoop.Info("Player 2 interaction error: " + e.Message);
            }
        }

        /// <summary>Hide any prompts Player 2 is holding open (e.g. when Player 2 is hidden).</summary>
        public void ReleaseAll(HeroController hero)
        {
            if (hero == null) { armed.Clear(); return; }
            Collider2D heroCol = hero.GetComponent<Collider2D>();
            if (heroCol != null)
            {
                Vector2 heroPos = heroCol.bounds.center;
                foreach (Collider2D col in armed)
                {
                    if (col != null && !col.OverlapPoint(heroPos))
                    {
                        try { SendBoth(col, "OnTriggerExit2D", heroCol); }
                        catch (Exception) { }
                    }
                }
            }
            armed.Clear();
            armedFsmObject.Clear();
        }

        void SendBoth(Collider2D col, string method, Collider2D heroCol)
        {
            Send(col.gameObject, method, heroCol);
            GameObject fsmGo;
            if (armedFsmObject.TryGetValue(col, out fsmGo) && fsmGo != null && fsmGo != col.gameObject)
                Send(fsmGo, method, heroCol);
        }

        /// <summary>Logs (once per object) what Player 2 is standing in, to help diagnose prompts.</summary>
        void LogNear(Component fsm, Collider2D col, bool prompt)
        {
            string fsmName = nameProp == null ? "?" : (nameProp.GetValue(fsm, null) as string ?? "?");
            string key = fsm.gameObject.name + "/" + fsmName;
            if (loggedNear.Count > 200 || !loggedNear.Add(key)) return;
            LocalCoop.Info("Player 2 is in '" + col.gameObject.name + "' (FSM '" + fsmName + "' on '" +
                           fsm.gameObject.name + "'): " + (prompt ? "prompt" : "not a prompt"));
        }

        static bool Init()
        {
            if (initDone) return fsmType != null;
            initDone = true;
            fsmType = Type.GetType("PlayMakerFSM, PlayMaker");
            if (fsmType == null) return false;
            statesProp = fsmType.GetProperty("FsmStates");
            nameProp = fsmType.GetProperty("FsmName");
            Type stateType = statesProp == null ? null : statesProp.PropertyType.GetElementType();
            actionsProp = stateType == null ? null : stateType.GetProperty("Actions");
            if (actionsProp == null) fsmType = null;
            LocalCoop.Info("Player 2 interactions: " + (fsmType != null ? "ok" : "unavailable"));
            return fsmType != null;
        }

        /// <summary>An FSM that shows a prompt and waits for up.</summary>
        bool IsPrompt(Component fsm)
        {
            bool result;
            if (promptCache.TryGetValue(fsm, out result)) return result;
            result = false;

            // Never enemies.
            string fsmName = nameProp == null ? "" : (nameProp.GetValue(fsm, null) as string ?? "");
            if (fsmName.Contains("Bench")) result = true;

            if (!result && fsm.GetComponent<HealthManager>() == null && fsm.GetComponentInParent<HealthManager>() == null)
            {
                var states = statesProp.GetValue(fsm, null) as Array;
                if (states != null)
                {
                    foreach (object state in states)
                    {
                        var actions = state == null ? null : actionsProp.GetValue(state, null) as Array;
                        if (actions == null) continue;
                        foreach (object a in actions)
                        {
                            if (a == null) continue;
                            string n = a.GetType().Name;
                            if (n.Contains("ListenForUp") || n.Contains("Prompt"))
                            {
                                result = true;
                                break;
                            }
                        }
                        if (result) break;
                    }
                }
            }

            if (promptCache.Count > 512) promptCache.Clear();
            promptCache[fsm] = result;
            return result;
        }

        static void Send(GameObject go, string method, Collider2D heroCol)
        {
            MonoBehaviour[] comps = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                MethodInfo m = comps[i].GetType().GetMethod(method, AnyInstance, null, new[] { typeof(Collider2D) }, null);
                if (m != null) m.Invoke(comps[i], new object[] { heroCol });
            }
        }
    }
}
