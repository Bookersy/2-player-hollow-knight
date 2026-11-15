using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Lets enemies notice and go after Player 2.
    ///
    /// Enemy AI is mostly PlayMaker FSMs that keep "the Knight" in a GameObject variable
    /// and chase/face/aim at whatever that variable points to. When Player 2 is clearly
    /// closer to an enemy, we point those variables at Player 2, and back again when the
    /// Knight is closer. Enemies that wait for the Knight to enter their AlertRange are
    /// also woken up when Player 2 walks into it.
    /// </summary>
    internal class EnemyTargeting
    {
        const float Interval = 0.2f;
        const float Hysteresis = 1.5f; // stops enemies flip-flopping between players

        static bool initDone, broken;
        static Type fsmType, alertType;
        static PropertyInfo fsmVarsProp, goVarsProp, goValueProp;
        static FieldInfo alertInRange;

        float timer;
        bool anySwapped;
        readonly List<Component> forcedAlerts = new List<Component>();

        public void Update(GameObject p2, HeroController hero, bool p2Active, Vector2 p2Center, float dt)
        {
            if (!LocalCoop.Settings.EnemiesTargetP2 || broken || !p2Active)
            {
                Restore(p2, hero);
                return;
            }

            timer -= dt;
            if (timer > 0f) return;
            timer = Interval;

            try
            {
                if (!Init()) return;
                Retarget(p2, hero);
                WakeAlerts(p2Center, hero);
            }
            catch (Exception e)
            {
                broken = true;
                LocalCoop.Info("Enemy targeting turned off after an error: " + e);
                Restore(p2, hero);
            }
        }

        public void Restore(GameObject p2, HeroController hero)
        {
            if (hero == null) return;
            try
            {
                if (anySwapped && Init())
                {
                    ForEachTargetVar((fsm, v, val) =>
                    {
                        if (val != null && val == p2) goValueProp.SetValue(v, hero.gameObject, null);
                    });
                }
                anySwapped = false;

                if (alertInRange != null)
                {
                    Vector2 heroPos = hero.transform.position;
                    foreach (Component a in forcedAlerts)
                    {
                        if (a != null) alertInRange.SetValue(a, Contains(a, heroPos));
                    }
                }
                forcedAlerts.Clear();
            }
            catch (Exception e)
            {
                LocalCoop.Info("Enemy targeting restore error: " + e.Message);
            }
        }

        static bool Init()
        {
            if (initDone) return fsmType != null;
            initDone = true;

            fsmType = Type.GetType("PlayMakerFSM, PlayMaker");
            if (fsmType != null)
            {
                fsmVarsProp = fsmType.GetProperty("FsmVariables");
                Type varsType = fsmVarsProp == null ? null : fsmVarsProp.PropertyType;
                goVarsProp = varsType == null ? null : varsType.GetProperty("GameObjectVariables");
                Type goVarType = goVarsProp == null ? null : goVarsProp.PropertyType.GetElementType();
                goValueProp = goVarType == null ? null : goVarType.GetProperty("Value");
                if (goValueProp == null) fsmType = null;
            }

            alertType = typeof(HealthManager).Assembly.GetType("AlertRange");
            if (alertType != null)
            {
                foreach (FieldInfo f in alertType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(bool) && f.Name.ToLower().Contains("inrange"))
                    {
                        alertInRange = f;
                        break;
                    }
                }
            }

            LocalCoop.Info("Enemy targeting: FSMs " + (fsmType != null ? "ok" : "unavailable") +
                           ", alert ranges " + (alertInRange != null ? "ok (" + alertInRange.Name + ")" : "unavailable"));
            return fsmType != null;
        }

        delegate void TargetVarAction(Component fsm, object variable, GameObject value);

        static void ForEachTargetVar(TargetVarAction action)
        {
            UnityEngine.Object[] fsms = UnityEngine.Object.FindObjectsOfType(fsmType);
            for (int i = 0; i < fsms.Length; i++)
            {
                var fsm = fsms[i] as Component;
                if (fsm == null) continue;
                // Only enemies: leave NPCs, doors, benches etc. alone.
                if (fsm.GetComponent<HealthManager>() == null && fsm.GetComponentInParent<HealthManager>() == null) continue;

                object vars = fsmVarsProp.GetValue(fsm, null);
                var arr = vars == null ? null : goVarsProp.GetValue(vars, null) as Array;
                if (arr == null) continue;
                foreach (object v in arr)
                {
                    if (v == null) continue;
                    action(fsm, v, goValueProp.GetValue(v, null) as GameObject);
                }
            }
        }

        void Retarget(GameObject p2, HeroController hero)
        {
            GameObject heroGo = hero.gameObject;
            Vector2 p1 = hero.transform.position;
            Vector2 p2pos = p2.transform.position;

            ForEachTargetVar((fsm, v, val) =>
            {
                if (val == null) return;
                bool isHero = val == heroGo;
                bool isP2 = val == p2;
                if (!isHero && !isP2) return;

                Vector2 e = fsm.transform.position;
                float d1 = Vector2.Distance(e, p1);
                float d2 = Vector2.Distance(e, p2pos);

                if (isHero && d2 + Hysteresis < d1)
                {
                    goValueProp.SetValue(v, p2, null);
                    anySwapped = true;
                }
                else if (isP2 && d1 + Hysteresis < d2)
                {
                    goValueProp.SetValue(v, heroGo, null);
                }
            });
        }

        void WakeAlerts(Vector2 p2Center, HeroController hero)
        {
            if (alertType == null || alertInRange == null) return;
            Vector2 heroPos = hero.transform.position;

            forcedAlerts.RemoveAll(a => a == null);
            UnityEngine.Object[] alerts = UnityEngine.Object.FindObjectsOfType(alertType);
            for (int i = 0; i < alerts.Length; i++)
            {
                var a = alerts[i] as Component;
                if (a == null) continue;
                bool forced = forcedAlerts.Contains(a);
                if (Contains(a, p2Center))
                {
                    alertInRange.SetValue(a, true);
                    if (!forced) forcedAlerts.Add(a);
                }
                else if (forced)
                {
                    alertInRange.SetValue(a, Contains(a, heroPos));
                    forcedAlerts.Remove(a);
                }
            }
        }

        static bool Contains(Component c, Vector2 point)
        {
            Collider2D[] cols = c.GetComponents<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].enabled && cols[i].OverlapPoint(point)) return true;
            }
            return false;
        }
    }
}
