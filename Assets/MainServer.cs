using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainServer : SingletonBehaviour<MainServer>
{
    [Header("Debug Setting")]
    public bool DisableLog = false;
    public bool DisableLogWarning = false;
    public bool DisableSendLog = false;
    public bool DisableReceiveLog = false;


    private HashSet<UserToken> userList;
    public HashSet<UserToken> removedUserListBuffer; // Socket Close전에 버퍼에 담아 한번에 처리
    private HashSet<GameRoom> gameRoomList;

    private uint nextUid = 1;
    private uint nextRoomID = 1;

    private MatchQueue match;
    public MatchQueue MatchQueue => match;

    private void Awake()
    {
        userList = new();
        removedUserListBuffer = new();
        gameRoomList = new();
        match = new();
    }

    private void FixedUpdate()
    {
        if (removedUserListBuffer.Count > 0)
        {
            DeleteUser();
        }

        if (match.TicketList.Count > 0)
        {
            match.DoMatch();
        }
    }

    public UserToken OnNewClient()
    {
        GameObject obj = new();
        var token = obj.AddComponent<UserToken>();
        obj.name = $"ClientNotLogined";
        userList.Add(token);

        token.AddOnReceivedDelegate(BasicProcessPacket);
        token.AddOnReceivedDelegate(ReceiveLoginData, "Login");

        MyDebug.Log($"Hello client! User count: {userList.Count}");
        return token;
    }

    public void OnLogin(UserToken u)
    {
        u.AddOnReceivedDelegate(ReceiveLogoutData, "Logout");
        u.AddOnReceivedDelegate(SyncVarActions);
        u.AddOnReceivedDelegate(ReceiveMatchmakingEnter, "MatchmakingEnter");
        u.RemoveOnReceivedDelegate("Login");
    }

    public void OnLogout(UserToken u)
    {
        u.AddOnReceivedDelegate(ReceiveLoginData, "Login");
        u.RemoveOnReceivedDelegate("Logout");
    }

    private void DeleteUser()
    {
        foreach (var user in removedUserListBuffer)
        {
            if (user.Room != null)
            {
                user.Room.BreakRoom(user);
            }
            userList.Remove(user);
            ListenServer.Inst.DisconnectClient(user);
            user.Close();
        }

        MyDebug.Log($"User counter: {userList.Count}");
        removedUserListBuffer.Clear();
    }

    public void ApplyRoomIncluded(UserToken user)
    {
        user.RemoveOnReceivedDelegate("MatchmakingEnter");
        if (user.Room != null)
        {
            user.AddOnReceivedDelegate(ReceiveRoomControl, "RoomControl");
        }
        else
        {
            user.RemoveOnReceivedDelegate("RoomControl");
            user.AddOnReceivedDelegate(ReceiveMatchmakingEnter, "MatchmakingEnter");
        }
    }

    public void EnterGameRoom(UserToken user1, UserToken user2)
    {
        var newRoom = new GameRoom(nextRoomID++);
        gameRoomList.Add(newRoom);

        // TODO: 랜덤으로 유저 1과 2중 한쪽을 먼저 enter시켜야함.

        newRoom.UserEnter(user1);
        newRoom.UserEnter(user2);
    }

    private void ReceiveMatchmakingEnter(UserToken user, Packet p)
    {
        if (p.Type != (short)PacketType.ROOM_CONTROL) return;

        var message = MessagePacket.Deserialize(p.Data);

        switch (message.message)
        {
            case "ENTER":
                MyDebug.Log($"[{user.UID}] start matchmaking");
                Inst.MatchQueue.AddTicket(
                    new MatchQueue.Ticket(user, 1000, Time.time));
                break;
            case "EXIT":
                Inst.MatchQueue.RemoveTicket(user);
                break;
            default:
                break;
        }
    }

    private void ReceiveRoomControl(UserToken user, Packet p)
    {
        if (p.Type != (short)PacketType.ROOM_CONTROL) return;

        var message = MessagePacket.Deserialize(p.Data);

        if (user.UID != message.senderID)
        {
            MyDebug.LogError($"[{user.UID}] sender id not matched!");
            user.Room.BreakRoom(user);
            return;
        }

        switch (message.message)
        {
            case "LOADED":
                if (user.Room.ReadyId == 0) user.Room.ReadyId = message.senderID;
                else if (user.Room.ReadyId != message.senderID) user.Room.StartGame();
                break;
            case "BREAK":
                user.Room.EndGame(user);
                break;
        }
    }

    private void ReceiveLoginData(UserToken u, Packet p)
    {
        if (p.Type != (short)PacketType.USER_LOGIN) return;
        var msg = MessagePacket.Deserialize(p.Data);
        if (msg.senderID != 1) return;

        DatabaseManager.Inst.TryLogin(msg.message, (data, isSuccess) =>
        {
            var loginData = isSuccess ? DatabaseManager.Inst.PackUserData(data) : new UserDataPacket();
            loginData.isSuccess = isSuccess;
            var resPacket = new Packet().Pack(PacketType.USER_LOGIN, loginData);
            u.Send(resPacket);

            if (isSuccess)
            {
                //u.Login(data.uid);
            }
        });
    }

    private void ReceiveLogoutData(UserToken u, Packet p)
    {
        if (p.Type != (short)PacketType.USER_LOGIN) return;
        var senderId = MessagePacket.Deserialize(p.Data).senderID;

        if (senderId == 0)
        {
            u.Logout();
        }
    }

    private void SyncVarActions(UserToken user, Packet packet)
    {
        switch ((PacketType)packet.Type)
        {
            case PacketType.SYNCVAR_INIT:
                if (user.Room == null) return;
                user.Room.InitSyncVar(user, SyncVarPacket.Deserialize(packet.Data));
                break;
            case PacketType.SYNCVAR_CHANGE:
                if (user.Room == null) return;
                user.Room.ChangeSyncVar(user, SyncVarPacket.Deserialize(packet.Data));
                break;
        }
    }

    private void BasicProcessPacket(UserToken user, Packet packet)
    {
        if (packet.Type != (short)PacketType.PACKET_TEST) return;
        var message = TestPacket.Deserialize(packet.Data).message;
        MyDebug.Log($"[{user.UID}] PACKET_TEST: {message}");
        user.Send(packet);
    }
}
