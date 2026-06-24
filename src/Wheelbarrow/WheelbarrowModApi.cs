using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    [Preserve]
    public class WheelbarrowModApi : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            new Harmony("com.pathof7d2d.wheelbarrow").PatchAll(typeof(WheelbarrowModApi).Assembly);

            if (GameObject.Find("WheelbarrowVisualRepair") == null)
            {
                GameObject repairObject = new GameObject("WheelbarrowVisualRepair");
                Object.DontDestroyOnLoad(repairObject);
                repairObject.AddComponent<WheelbarrowVisualRepairBehaviour>();
                repairObject.AddComponent<WheelbarrowPushBehaviour>();
            }

            Log.Out("[Wheelbarrow] Loaded. Press the interact key on a wheelbarrow to push it.");
        }
    }
}
