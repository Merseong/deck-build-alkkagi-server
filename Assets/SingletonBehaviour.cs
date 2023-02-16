using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _inst;

    public static bool IsEnabled
    {
        get
        {
            if (_inst)
            {
                return true;
            }
            return false;
        }
    }

    public static T Inst
    {
        get
        {
            if (_inst)
            {
                return _inst;
            }
            _inst = FindObjectOfType<T>();
            if (!_inst)
            {
                GameObject obj = new();
                obj.name = typeof(T).FullName + "_Singleton";
                _inst = obj.AddComponent<T>();
            }
            return _inst;
        }
    }
}
