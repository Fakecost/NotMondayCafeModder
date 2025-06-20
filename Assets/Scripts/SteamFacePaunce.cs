using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SteamFacePaunce : MonoBehaviour
{
    public uint appID;

    private void Awake()
    {
        DontDestroyOnLoad(this);
        try
        {
            Steamworks.SteamClient.Init(appID);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void OnApplicationQuit()
    {
        try
        {
            Steamworks.SteamClient.Shutdown();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
