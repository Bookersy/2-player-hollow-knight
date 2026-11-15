using System;
using System.Collections.Generic;
using System.Reflection;
using InControl;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Reflection-based access to the few game/InControl members whose exact shape
    /// (field vs property) varies between versions, so the mod doesn't break on them.
    /// </summary>
    internal static class Game
    {
        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static Func<object> heroGetter, gmGetter, devicesGetter, nullDeviceGetter;
        static readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
        static readonly List<InputDevice> NoDevices = new List<InputDevice>();

        // "_instance" first: the public "instance" getters log errors when nothing exists yet.
        public static HeroController Hero
        {
            get
            {
                if (heroGetter == null)
                    heroGetter = StaticGetter(typeof(HeroController), "_instance", "SilentInstance", "instance");
                return heroGetter() as HeroController;
            }
        }

        public static GameManager GM
        {
            get
            {
                if (gmGetter == null)
                    gmGetter = StaticGetter(typeof(GameManager), "_instance", "instance");
                return gmGetter() as GameManager;
            }
        }

        public static bool IsPlaying
        {
            get
            {
                GameManager gm = GM;
                return gm != null && gm.gameState.ToString() == "PLAYING";
            }
        }

        public static IList<InputDevice> Devices()
        {
            if (devicesGetter == null) devicesGetter = StaticGetter(typeof(InputManager), "Devices");
            var list = devicesGetter() as IList<InputDevice>;
            return list ?? NoDevices;
        }

        public static InputDevice NullDevice
        {
            get
            {
                if (nullDeviceGetter == null) nullDeviceGetter = StaticGetter(typeof(InputDevice), "Null");
                return nullDeviceGetter() as InputDevice;
            }
        }

        public static InputControlType? Control(string name)
        {
            try
            {
                if (Enum.IsDefined(typeof(InputControlType), name))
                    return (InputControlType)Enum.Parse(typeof(InputControlType), name);
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------ the game's controller bindings

        static InputHandler inputHandler;

        public static object HeroActions()
        {
            if (inputHandler == null) inputHandler = UnityEngine.Object.FindObjectOfType<InputHandler>();
            return inputHandler == null ? null : Get(inputHandler, "inputActions");
        }

        /// <summary>
        /// The controller buttons/sticks the game has bound to one of the Knight's actions
        /// (e.g. "jump", "attack", "dash", "left"), as set in Options → Controller.
        /// </summary>
        public static List<InputControlType> BoundControls(string actionName)
        {
            var result = new List<InputControlType>();
            object action = Get(HeroActions(), actionName);
            var bindings = Get(action, "Bindings") as System.Collections.IEnumerable;
            if (bindings == null) return result;

            foreach (object b in bindings)
            {
                if (b == null || b.GetType().Name != "DeviceBindingSource") continue;
                object control = Get(b, "Control");
                if (control is InputControlType && !result.Contains((InputControlType)control))
                    result.Add((InputControlType)control);
            }
            return result;
        }

        // ------------------------------------------------------------ generic member access

        static Func<object> StaticGetter(Type t, params string[] names)
        {
            foreach (string n in names)
            {
                FieldInfo f = t.GetField(n, AnyStatic);
                if (f != null) return () => f.GetValue(null);
                PropertyInfo p = t.GetProperty(n, AnyStatic);
                if (p != null && p.CanRead) return () => p.GetValue(null, null);
            }
            LocalCoop.Info("Couldn't find " + t.Name + "." + string.Join("/", names));
            return () => null;
        }

        static MemberInfo FindMember(Type t, string name)
        {
            string key = t.FullName + "::" + name;
            MemberInfo m;
            if (memberCache.TryGetValue(key, out m)) return m;
            m = t.GetField(name, AnyInstance);
            if (m == null) m = t.GetProperty(name, AnyInstance);
            memberCache[key] = m;
            return m;
        }

        public static bool HasMember(object o, string name)
        {
            return o != null && FindMember(o.GetType(), name) != null;
        }

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            MemberInfo m = FindMember(o.GetType(), name);
            var f = m as FieldInfo;
            if (f != null) return f.GetValue(o);
            var p = m as PropertyInfo;
            if (p != null && p.CanRead) return p.GetValue(o, null);
            return null;
        }

        /// <summary>Works on boxed structs too (modifies the box).</summary>
        public static bool Set(object o, string name, object value)
        {
            if (o == null) return false;
            MemberInfo m = FindMember(o.GetType(), name);
            var f = m as FieldInfo;
            if (f != null)
            {
                f.SetValue(o, value);
                return true;
            }
            var p = m as PropertyInfo;
            if (p != null && p.CanWrite)
            {
                p.SetValue(o, value, null);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------ hitting enemies

        static Type hitType, attackTypesType;
        static MethodInfo hitMethod;
        static bool hitBroken;

        public static void HitEnemy(HealthManager hm, GameObject source, int damage, float direction)
        {
            if (hitBroken) return;
            if (hitMethod == null)
            {
                try
                {
                    Assembly asm = typeof(HealthManager).Assembly;
                    hitType = asm.GetType("HitInstance");
                    attackTypesType = asm.GetType("AttackTypes");
                    hitMethod = typeof(HealthManager).GetMethod("Hit", new[] { hitType });
                    if (hitMethod == null) throw new MissingMethodException("HealthManager", "Hit");
                }
                catch (Exception e)
                {
                    hitBroken = true;
                    LocalCoop.Info("Player 2 can't hit enemies in this game version: " + e);
                    return;
                }
            }

            try
            {
                object hit = Activator.CreateInstance(hitType);
                Set(hit, "Source", source);
                Set(hit, "AttackType", Enum.Parse(attackTypesType, "Nail"));
                Set(hit, "DamageDealt", damage);
                Set(hit, "Direction", direction);
                Set(hit, "Multiplier", 1f);
                Set(hit, "MagnitudeMultiplier", 1f);
                Set(hit, "IgnoreInvulnerable", false);
                hitMethod.Invoke(hm, new[] { hit });
            }
            catch (Exception e)
            {
                LocalCoop.Info("Error hitting enemy: " + e.Message);
            }
        }
    }
}
