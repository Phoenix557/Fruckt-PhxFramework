using MelonLoader;

[assembly: MelonInfo(typeof(Phx.PhxPlugin), "Phx", "1.0.0", "github.com/Phoenix557")]
[assembly: MelonGame("tripledose", "FRUKT")]
[assembly: MelonPriority(-100)]

namespace Phx
{
    public class PhxPlugin : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Config.EnsureLoaded();
            HarmonyInstance.PatchAll(typeof(PhxBackPatch));
            LoggerInstance.Msg("Loaded. Open PHX in the pause menu for mod settings.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Menu.Reset();
        }

        public override void OnUpdate()
        {
            Menu.Tick();
        }
    }
}
