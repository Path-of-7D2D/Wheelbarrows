using UnityEngine;
using UnityEngine.Scripting;

namespace Wheelbarrow
{
    [Preserve]
    public class WheelbarrowModApi : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            if (GameObject.Find("WheelbarrowVisualRepair") == null)
            {
                GameObject repairObject = new GameObject("WheelbarrowVisualRepair");
                Object.DontDestroyOnLoad(repairObject);
                repairObject.AddComponent<WheelbarrowVisualRepairBehaviour>();
                repairObject.AddComponent<WheelbarrowPushBehaviour>();
            }

            Log.Out("[Wheelbarrow] Loaded. 'wb' spawns a test cart; 'wb push' / 'wb drop' toggle push mode.");
        }
    }
}
