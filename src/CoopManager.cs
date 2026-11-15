using System;
using System.Collections.Generic;
using InControl;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Spawns/despawns Player 2, hands it a controller, and stops that controller
    /// from also driving the Knight.
    /// </summary>
    public class CoopManager : MonoBehaviour
    {
        PlayerTwo p2;
        InputHandler inputHandler;
        KeyCode toggleKey = KeyCode.F9;
        bool p1Isolated;
        bool warnedNoDeviceProp;
        string lastDeviceName;

        void Start()
        {
            try
            {
                toggleKey = (KeyCode)Enum.Parse(typeof(KeyCode), LocalCoop.Settings.ToggleKey, true);
            }
            catch (Exception)
            {
                LocalCoop.Info("Unknown ToggleKey '" + LocalCoop.Settings.ToggleKey + "', using F9.");
                toggleKey = KeyCode.F9;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                LocalCoop.Settings.Enabled = !LocalCoop.Settings.Enabled;
                LocalCoop.Info("Co-op " + (LocalCoop.Settings.Enabled ? "on" : "off"));
            }

            HeroController hero = Game.Hero;
            bool active = LocalCoop.Settings.Enabled && hero != null && Game.GM != null;

            if (!active)
            {
                if (p2 != null)
                {
                    Destroy(p2.gameObject);
                    p2 = null;
                }
                RestoreP1Input();
                return;
            }

            IList<InputDevice> devices = Game.Devices();
            InputDevice p2Device = FindP2Device(devices);
            IsolateP1Input(devices);

            if (p2 == null)
            {
                p2 = PlayerTwo.Create(hero);
                if (p2 == null) return;
            }
            p2.Device = p2Device;

            string name = p2Device == null ? "(none)" : (Game.Get(p2Device, "Name") as string ?? "controller");
            if (name != lastDeviceName)
            {
                lastDeviceName = name;
                LocalCoop.Info("Player 2 controller: " + name);
            }
        }

        void OnDestroy()
        {
            RestoreP1Input();
        }

        static InputDevice FindP1Device(IList<InputDevice> devices)
        {
            if (LocalCoop.Settings.P1UsesKeyboard) return null;
            return devices.Count > 0 ? devices[0] : null;
        }

        static InputDevice FindP2Device(IList<InputDevice> devices)
        {
            int skip = LocalCoop.Settings.P1UsesKeyboard ? -1 : 0;
            int n = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                if (i == skip) continue;
                if (n == LocalCoop.Settings.P2ControllerIndex) return devices[i];
                n++;
            }
            return null;
        }

        object GetP1Actions()
        {
            if (inputHandler == null) inputHandler = FindObjectOfType<InputHandler>();
            if (inputHandler == null) return null;
            return Game.Get(inputHandler, "inputActions");
        }

        /// <summary>
        /// InControl's PlayerActionSet.Device: when set, the Knight's controls only listen
        /// to that device. For keyboard-only Player 1 we point it at InControl's "null"
        /// device; keyboard bindings keep working because they don't go through a device.
        /// </summary>
        void IsolateP1Input(IList<InputDevice> devices)
        {
            object actions = GetP1Actions();
            if (actions == null) return;

            if (!Game.HasMember(actions, "Device"))
            {
                if (!warnedNoDeviceProp)
                {
                    warnedNoDeviceProp = true;
                    LocalCoop.Info("Can't lock Player 1 to one device in this game version; " +
                                   "Player 2's controller may also move the Knight.");
                }
                return;
            }

            InputDevice wanted = FindP1Device(devices) ?? Game.NullDevice;
            if (wanted == null) return;
            if (!ReferenceEquals(Game.Get(actions, "Device"), wanted))
            {
                Game.Set(actions, "Device", wanted);
            }
            p1Isolated = true;
        }

        void RestoreP1Input()
        {
            if (!p1Isolated) return;
            p1Isolated = false;
            object actions = GetP1Actions();
            // null = back to InControl's usual "whichever device was used last".
            if (actions != null) Game.Set(actions, "Device", null);
        }
    }
}
