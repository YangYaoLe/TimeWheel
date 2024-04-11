using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class GameMain : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        TimerManager.Instance.Init();
    }

    // Update is called once per frame
    void Update()
    {
        TimerManager.Instance.Update();
    }

    private void OnApplicationQuit()
    {
        
    }
}
