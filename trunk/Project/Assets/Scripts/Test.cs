using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    public Button addBtn;
    public TMP_InputField delayInput;
    public TMP_InputField intervalInput;
    public TMP_InputField looptimesInput;
    public TMP_InputField param1Input;

    // Start is called before the first frame update
    void Start()
    {
        if (addBtn != null)
        {
            addBtn.onClick.AddListener(OnClickAddBtn);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnClickAddBtn()
    {
        float delay = 0;
        float interval = 0;
        int looptimes = -1;
        float.TryParse(delayInput.text, out delay);
        float.TryParse(intervalInput.text, out interval);
        int.TryParse(looptimesInput.text, out looptimes);
        string[] param = { param1Input.text };
        TimerManager.Instance.AddTimer(TestTimerFunc, interval, delay > 0, looptimes, param);
    }

    void TestTimerFunc(object[] param = null)
    {
        Debug.Log("������һ�ζ�ʱ������" + param[0] +", ��ǰʱ�䣺" + Time.time + " ,��ǰ֡����" + Time.frameCount);
    }
}
