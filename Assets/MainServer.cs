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

    private int nextUid = 1;
    private int nextRoomID = 1;

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
        token.Login(nextUid++);
        obj.name = $"Client{token.UID}";
        userList.Add(token);

        token.AddOnReceivedDelegate(ReceiveFirstPacket, "FirstPacket");
        token.AddOnReceivedDelegate(BasicProcessPacket);
        token.AddOnReceivedDelegate(SyncVarActions);
        token.AddOnReceivedDelegate(ReceiveMatchmakingEnter, "MatchmakingEnter");

        MyDebug.Log($"Hello client! {token.UID}, User count: {userList.Count}");
        return token;
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

    // on receive room_enter packet from user
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

        switch (message.message)
        {
            case "LOADED":
                if (user.Room.ReadyId < 0) user.Room.ReadyId = message.senderID;
                else if (user.Room.ReadyId != message.senderID) user.Room.StartGame();
                break;
            case "BREAK":
                user.Room.EndGame(user);
                break;
        }
    }

    private void ReceiveFirstPacket(UserToken u, Packet _)
    {
        var infoData = new MessagePacket();
        infoData.senderID = u.UID;
        infoData.message = "";
        var infoPacket = new Packet().Pack(PacketType.PACKET_INFO, infoData);
        u.Send(infoPacket);

        u.RemoveOnReceivedDelegate("FirstPacket");
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
        switch ((PacketType)packet.Type)
        {
            case PacketType.PACKET_USER_CLOSED:
                if (user.Room != null)
                {
                    user.Room.BreakRoom(user);
                }
                removedUserListBuffer.Add(user);
                break;
            case PacketType.PACKET_TEST:
                var message = TestPacket.Deserialize(packet.Data).message;
                MyDebug.Log($"[{user.UID}] PACKET_TEST: {message}");
                user.Send(packet);
                break;
        }
    }
}
