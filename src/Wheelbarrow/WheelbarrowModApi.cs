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
            }

            Log.Out("[Wheelbarrow] Loaded. Use 'wb' to spawn a test wheelbarrow near you.");
        }
    }
}
