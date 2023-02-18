using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class MyDebug : MonoBehaviour
{
    public static void Log(object obj)
    {
        if (MainServer.Inst.DisableLog) return;
#if UNITY_EDITOR
        Debug.Log(obj);
#elif UNITY_SERVER
        Console.WriteLine(obj.ToString());
#endif
    }

    public static void LogError(object obj)
    {
        if (MainServer.Inst.DisableLogWarning) return;
#if UNITY_EDITOR
        Debug.LogError(obj);
#elif UNITY_SERVER
        Console.WriteLine(obj.ToString());
#endif
    }
}
