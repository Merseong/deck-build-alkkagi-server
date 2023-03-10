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

    private HashSet<UserToken> connectedOnlyUserList;
    private Dictionary<uint, UserToken> userList;
    private string connectionCountString => $"{connectedOnlyUserList.Count} / {userList.Count} logined";
    public HashSet<UserToken> removedUserListBuffer; // Socket Close전에 버퍼에 담아 한번에 처리
    private HashSet<GameRoom> gameRoomList;

    private uint nextRoomID = 1;

    private MatchQueue match;
    public MatchQueue MatchQueue => match;

    private void Awake()
    {
        connectedOnlyUserList = new();
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
        connectedOnlyUserList.Add(token);

        token.AddOnReceivedDelegate(BasicProcessPacket);
        token.AddOnReceivedDelegate(ReceiveLoginData, "Login");

        MyDebug.Log($"Hello client! Connection : {connectionCountString}");
        return token;
    }

    public void OnLogin(UserToken u)
    {
        u.AddOnReceivedDelegate(ReceiveLogoutData, "Logout");
        u.AddOnReceivedDelegate(ReceiveProblemReportAction);
        u.AddOnReceivedDelegate(ReceiveUserInfo, "UserInfo");
        u.AddOnReceivedDelegate(ReceiveUserAction, "UserAction");
        u.AddOnReceivedDelegate(SyncVarActions);
        u.AddOnReceivedDelegate(ReceiveMatchmakingEnter, "MatchmakingEnter");
        u.RemoveOnReceivedDelegate("Login");
    }

    public void OnLogout(UserToken u)
    {
        // after clear delegate list
        u.AddOnReceivedDelegate(ReceiveLoginData, "Login");
    }

    private void DeleteUser()
    {
        foreach (var user in removedUserListBuffer)
        {
            if (userList.TryGetValue(user.UID, out var foundUser) &&
                foundUser == user)
            {
                userList.Remove(user.UID);
            }
            connectedOnlyUserList.Remove(user);
            user.Logout();
            ListenServer.Inst.DisconnectClient(user);
            user.Close();
        }

        MyDebug.Log($"Connection : {connectionCountString}");
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

    public void EnterGameRoom(MatchQueue.Ticket user1, MatchQueue.Ticket user2)
    {
        var newRoom = new GameRoom(nextRoomID++);
        gameRoomList.Add(newRoom);

        // TODO: 랜덤으로 유저 1과 2중 한쪽을 먼저 enter시켜야함.
        newRoom.UserEnter(user1.user, user1.deckCode, user1.deckCount);
        newRoom.UserEnter(user2.user, user2.deckCode, user2.deckCount);
    }

    private void ReceiveMatchmakingEnter(UserToken user, Packet p)
    {
        if (p.Type != (short)PacketType.ROOM_CONTROL) return;

        var message = MessagePacket.Deserialize(p.Data);
        var msgArr = message.message.Split(' ');
        
        switch (msgArr[0])
        {
            case "ENTER/":
                // ENTER/ (deckCode) (deckCount)
                MyDebug.Log($"[{user.UID}] start matchmaking");
                Inst.MatchQueue.AddTicket(
                    new MatchQueue.Ticket(user, msgArr[1], (uint)user.UserData["rating"], Time.time, int.Parse(msgArr[2])));
                break;
            case "EXIT/":
                MyDebug.Log($"[{user.UID}] cancel matchmaking");
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
            user.Room.EndGame(user);
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

        if (msg.senderID == 1) // login action
        {
            DatabaseManager.Inst.TryLogin(msg.message, (data, isSuccess) =>
            {
                var loginData = isSuccess ? DatabaseManager.Inst.PackUserData(data) : new UserDataPacket();
                loginData.isSuccess = isSuccess;
                var resPacket = new Packet().Pack(PacketType.USER_LOGIN, loginData);
                u.Send(resPacket);

                if (isSuccess)
                {
                    connectedOnlyUserList.Remove(u);
                    // find logined
                    if (userList.TryGetValue(data.uid, out var loginedToken))
                    {
                        removedUserListBuffer.Add(loginedToken);
                        userList[data.uid] = u;
                    }
                    else
                    {
                        userList.Add(data.uid, u);
                    }

                    u.Login(data);
                }
                MyDebug.Log($"[id {data.loginId}] login {(isSuccess ? "success" : "failed")}");
            });
        }
        else if (msg.senderID == 0) // register action
        {
            // msg.message => loginId password nickname
            var dataArr = msg.message.Split(' ');
            if (dataArr.Length != 3)
            {
                SendResponse("false");
                return;
            }

            DatabaseManager.Inst.RegisterUser(new UserDataPacket { nickname = dataArr[2] }, dataArr[0], dataArr[1], (isSuccess) =>
            {
                SendResponse(isSuccess ? "true" : "false");
                MyDebug.Log($"[id {dataArr[0]}] register {(isSuccess ? "success" : "failed")}");
            });

            void SendResponse(string msg)
            {
                var resPacket = new Packet().Pack(PacketType.USER_LOGIN, new MessagePacket
                {
                    senderID = 0,
                    message = msg
                });
                u.Send(resPacket);
            }
        }
    }

    private void ReceiveLogoutData(UserToken u, Packet p)
    {
        if (p.Type != (short)PacketType.USER_LOGIN) return;
        var senderId = MessagePacket.Deserialize(p.Data).senderID;

        if (senderId == 0)
        {
            userList.Remove(u.UID);
            connectedOnlyUserList.Add(u);
            u.Logout();
        }
    }

    private void ReceiveUserInfo(UserToken sender, Packet p)
    {
        if (p.Type != (short)PacketType.USER_INFO) return;
        uint toFindUid = MessagePacket.Deserialize(p.Data).senderID;
        DatabaseManager.Inst.FindUser(toFindUid, (foundUser) =>
        {
            if (foundUser == null)
            {
                var resPacket = new Packet().Pack(PacketType.USER_INFO, new UserDataPacket
                {
                    isSuccess = false
                });
                sender.Send(resPacket);
            }
            else
            {
                var foundUserPacket = DatabaseManager.Inst.PackUserData(foundUser);
                foundUserPacket.isSuccess = true;
                var resPacket = new Packet().Pack(PacketType.USER_INFO, foundUserPacket);
                sender.Send(resPacket);
            }
        });
    }

    private void ReceiveUserAction(UserToken sender, Packet p)
    {
        if (p.Type != (short)PacketType.USER_ACTION) return;
        var msg = MessagePacket.Deserialize(p.Data);
        if (msg.senderID != sender.UID) return;

        var msgArr = msg.message.Split(' ');
        switch (msgArr[0])
        {
            case "SHOP/":
                // 구매물품(구매후상태) 사용한돈 사용한명예포인트
                // 검증
                if (!uint.TryParse(msgArr[2], out var usedMoney) ||
                    !uint.TryParse(msgArr[3], out var usedHonor))
                    break;
                if ((usedMoney > (uint)sender.UserData["moneyPoint"]) ||
                    (usedHonor > (uint)sender.UserData["honorPoint"]))
                    break;
                // 업데이트
                var updateDict = new Dictionary<string, object>
                {
                    ["deckUnlock"] = msgArr[1],
                    ["moneyPoint"] = (uint)sender.UserData["moneyPoint"] - usedMoney,
                    ["honorPoint"] = (uint)sender.UserData["honorPoint"] - usedHonor,
                };
                DatabaseManager.Inst.UpdateUser(sender.UID, updateDict);
                sender.UpdateUserData(updateDict);
                break;
        }
    }

    private void ReceiveProblemReportAction(UserToken sender, Packet p)
    {
        if (p.Type != (short)PacketType.PROBLEM_REPORT) return;
        var msg = MessagePacket.Deserialize(p.Data);
        if (msg.senderID != sender.UID) return;

        DatabaseManager.Inst.ReportProblem(sender.UID, msg.message);
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
