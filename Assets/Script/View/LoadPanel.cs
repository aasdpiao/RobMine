using UnityEngine;

namespace Assets.Script.View
{
    public class LoadPanel : MonoBehaviour {

        void Start ()
        {
            NetworkMgr.GetInstance().OnPreloadingResources();
        }
	
        void Update () {
	
        }
    }
}
