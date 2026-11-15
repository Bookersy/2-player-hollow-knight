using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>Saved to LocalCoop.GlobalSettings.json in the game's save folder.</summary>
    public class CoopSettings
    {
        /// <summary>Co-op on/off. Also toggled in-game with ToggleKey.</summary>
        public bool Enabled = true;

        /// <summary>
        /// true  = Player 1 plays on keyboard, Player 2 gets a controller.
        /// false = Player 1 gets the first controller, Player 2 gets the next one.
        /// </summary>
        public bool P1UsesKeyboard = true;

        /// <summary>Which of the remaining controllers Player 2 uses (0 = first free one).</summary>
        public int P2ControllerIndex = 0;

        public int P2MaxMasks = 5;
        public float RespawnSeconds = 5f;

        /// <summary>Bonus: Player 2 regains one mask after this many nail hits (0 = off; use Focus instead).</summary>
        public int NailHitsPerHeal = 0;

        /// <summary>Seconds Player 2 can be off screen before being pulled back to Player 1.</summary>
        public float LeashSeconds = 1.5f;

        public float TintR = 0.55f;
        public float TintG = 0.8f;
        public float TintB = 1f;

        /// <summary>Enemies notice and go after Player 2 when they're closer. Turn off if enemies act strangely.</summary>
        public bool EnemiesTargetP2 = true;

        /// <summary>A Unity KeyCode name, e.g. "F9".</summary>
        public string ToggleKey = "F9";
    }

    public class LocalCoop : Mod, IGlobalSettings<CoopSettings>, IMenuMod
    {
        public static LocalCoop Instance;
        public static CoopSettings Settings = new CoopSettings();

        public LocalCoop() : base("Local Coop")
        {
            Instance = this;
        }

        public override string GetVersion()
        {
            return "0.1.8";
        }

        public void OnLoadGlobal(CoopSettings s)
        {
            Settings = s ?? new CoopSettings();
        }

        public CoopSettings OnSaveGlobal()
        {
            return Settings;
        }

        // ---------------------------------------------------------------- in-game menu
        // Settings → Mods → Local Coop

        public bool ToggleButtonInsideMenu
        {
            get { return false; }
        }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            var list = new List<IMenuMod.MenuEntry>();
            if (toggleButtonEntry.HasValue) list.Add(toggleButtonEntry.Value);

            list.Add(new IMenuMod.MenuEntry(
                "Co-op",
                new[] { "Off", "On" },
                "Turn Player 2 on or off (also F9)",
                i => Settings.Enabled = i == 1,
                () => Settings.Enabled ? 1 : 0));

            list.Add(new IMenuMod.MenuEntry(
                "Player 1 uses",
                new[] { "Keyboard", "Controller" },
                "Controller: Player 1 gets the first controller, Player 2 the next",
                i => Settings.P1UsesKeyboard = i == 0,
                () => Settings.P1UsesKeyboard ? 0 : 1));

            list.Add(new IMenuMod.MenuEntry(
                "Player 2 controller",
                new[] { "1st free", "2nd free", "3rd free" },
                "Which of the remaining controllers Player 2 uses",
                i => Settings.P2ControllerIndex = i,
                () => Math.Max(0, Math.Min(2, Settings.P2ControllerIndex))));

            list.Add(new IMenuMod.MenuEntry(
                "Enemies chase Player 2",
                new[] { "Off", "On" },
                "Enemies go after Player 2 when closer (experimental)",
                i => Settings.EnemiesTargetP2 = i == 1,
                () => Settings.EnemiesTargetP2 ? 1 : 0));

            list.Add(new IMenuMod.MenuEntry(
                "Player 2 masks",
                new[] { "3", "4", "5", "6", "7", "8", "9" },
                "Player 2's maximum health",
                i => Settings.P2MaxMasks = i + 3,
                () => Math.Max(0, Math.Min(6, Settings.P2MaxMasks - 3))));

            return list;
        }

        public override void Initialize()
        {
            var go = new GameObject("LocalCoop Manager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CoopManager>();
            Log("Local Coop loaded. Toggle with " + Settings.ToggleKey + ".");
        }

        public static void Info(string msg)
        {
            if (Instance != null) Instance.Log(msg);
        }
    }
}
