// WebGL WebSocket bridge for TerritoryWs.jslib consumers.
// Attach to the same GameObject as UnityWsClient and set clientName to match.
using UnityEngine;

namespace Territory.Client.Unity
{
    public sealed class TerritoryWsBridge : MonoBehaviour
    {
        public UnityWsClient target;

        void Awake()
        {
            if (target == null) target = GetComponent<UnityWsClient>();
            gameObject.name = "TerritoryWs";
        }
    }
}
