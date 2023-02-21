using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Networking.Transport;
using UnityEngine;

public class UserToken : MonoBehaviour
{
    [SerializeField] private uint userId = 0;
    public uint UID => userId;
    public GameRoom Room;

    public int ServerIdx;

    private Dictionary<string, object> userData;
    public Dictionary<string, object> UserData => userData;

    public delegate void ProcessPacketDelegate(UserToken user, Packet p);
    private Dictionary<int, ProcessPacketDelegate> processPacket;
    private int nextProcessPacketId = 1;
    public Dictionary<string, int> ProcessPacketDict;

    public Queue<Packet> receivedPackets;

    private void Awake()
    {
        processPacket = new();
        receivedPackets = new();
        ProcessPacketDict = new();
    }

    private void LateUpdate()
    {
        if (receivedPackets.Count <= 0) return;
        var deleList = new List<ProcessPacketDelegate>(processPacket.Values);
        while (receivedPackets.Count > 0)
        {
            var packet = receivedPackets.Dequeue();
            foreach (var dele in deleList)
            {
                dele(this, packet);
            }
        }
    }

    public void AppendReceivedPacket(Packet p)
    {
        receivedPackets.Enqueue(p);
    }

    public int AddOnReceivedDelegate(ProcessPacketDelegate myDelegate, string dictString = "")
    {
        processPacket.Add(nextProcessPacketId, myDelegate);
        if (dictString.Length > 0) ProcessPacketDict.Add(dictString, nextProcessPacketId);
        return nextProcessPacketId++;
    }

    public bool RemoveOnReceivedDelegate(string deleName)
    {
        if (ProcessPacketDict.TryGetValue(deleName, out int deleId))
        {
            ProcessPacketDict.Remove(deleName);
            return processPacket.Remove(deleId);
        }
        return false;
    }

    public bool RemoveOnReceivedDelegate(int delegateId)
    {
        if (ProcessPacketDict.ContainsValue(delegateId))
        {
            foreach (var item in ProcessPacketDict.Where(kvp => kvp.Value == delegateId).ToList())
            {
                ProcessPacketDict.Remove(item.Key);
            }
        }
        return processPacket.Remove(delegateId);
    }

    public void Send(Packet packet)
    {
        ListenServer.Inst.AppendToSendQueue(this, packet);
    }

    public void Close()
    {
        Destroy(gameObject);
    }

    public void UpdateUserData(Dictionary<string, object> toUpdate)
    {
        foreach (var key in toUpdate.Keys)
        {
            if (UserData.ContainsKey(key))
                UserData[key] = toUpdate[key];
        }
    }

    public void Login(UserDataSchema userData)
    {
        if (userData.uid == 0) return;
        if (userId != 0) return;

        userId = userData.uid;
        this.userData = userData.GetDict();

        gameObject.name = $"Client{userId}";
        MyDebug.Log($"[{userId}] Login success");
        MainServer.Inst.OnLogin(this);
    }

    public void Logout()
    {
        userId = 0;
        userData = null;
        gameObject.name = $"ClientNotLogined";
        MainServer.Inst.MatchQueue.RemoveTicket(this);
        if (Room != null) Room.EndGame(this);
        processPacket.Clear();
        receivedPackets.Clear();
        ProcessPacketDict.Clear();
        MainServer.Inst.OnLogout(this);
    }
}
