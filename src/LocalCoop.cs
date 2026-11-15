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

    public class LocalCoop : Mod, IGlobalSettings<CoopSettings>
    {
        public static LocalCoop Instance;
        public static CoopSettings Settings = new CoopSettings();

        public LocalCoop() : base("Local Coop")
        {
            Instance = this;
        }

        public override string GetVersion()
        {
            return "0.1.7";
        }

        public void OnLoadGlobal(CoopSettings s)
        {
            Settings = s ?? new CoopSettings();
        }

        public CoopSettings OnSaveGlobal()
        {
            return Settings;
        }

        public override void Initialize()
        {
            var go = new GameObject("LocalCoop Manager");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CoopManager>();
            Log("Local Coop loaded. Toggle with " + Settings.ToggleKey + ".");
        }

        public static void Info(string msg)
        {
            if (Instance != null) Instance.Log(msg);
        }
    }
}
