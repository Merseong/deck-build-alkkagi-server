using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameRoomStatus
{
    INIT,
    READY,
    RUNNING,
    FINISH,
}

public class GameRoom
{
    // 게임 룸
    private uint gameRoomID;
    public uint GameRoomID => gameRoomID;
    private GameRoomStatus status;
    public GameRoomStatus RoomStatus => status;
    public uint ReadyId = 0;

    private List<UserToken> userList = new List<UserToken>(2);
    private string[] usingDeck = new string[2];

    private uint hsPlayerUid = 0;
    private Dictionary<uint, byte[]> syncVarDataDict = new Dictionary<uint, byte[]>();

    public GameRoom(uint roomId)
    {
        gameRoomID = roomId;
        status = GameRoomStatus.INIT;

        MyDebug.Log($"[MAIN] Room Created, room ID : {roomId}");
    }

    public bool UserEnter(UserToken user, string deckCode)
    {
        // max player
        if (userList.Count > 2) return false;

        // 방 생성 초기에만 입장 가능
        if (status != GameRoomStatus.INIT) return false;

        int idx = userList.Count;
        usingDeck[idx] = deckCode;
        userList.Add(user);
        user.Room = this;
        MainServer.Inst.ApplyRoomIncluded(user);

        var toSend = new MessagePacket
        {
            senderID = 0,
            message = $"ENTERED/ {gameRoomID}"
        };
        var packet = new Packet().Pack(PacketType.ROOM_CONTROL, toSend);
        user.Send(packet);

        MyDebug.Log($"[room{gameRoomID}] {user.UID} entered");

        if (userList.Count == 2)
        {
            status = GameRoomStatus.READY;
            // READY상태에서 양 클라이언트의 로딩이 완료되면 그때 RUNNING으로 넘겨야 함
        }

        return true;
    }

    private bool BreakRoom(UserToken breaker)
    {
        userList.ForEach(u =>
        {
            u.Room = null;
            u.RemoveOnReceivedDelegate("RoomOpponent");
            u.RemoveOnReceivedDelegate("RoomBroadcast");
            MainServer.Inst.ApplyRoomIncluded(u);

            var exitData = new MessagePacket
            {
                senderID = 0,
                message = $"EXIT/ {breaker.UID}"
            };
            Packet packet;

            switch (status)
            {
                case GameRoomStatus.INIT:
                case GameRoomStatus.READY:
                    packet = new Packet().Pack(PacketType.ROOM_CONTROL, exitData);
                    u.Send(packet);
                    break;
                case GameRoomStatus.RUNNING:
                case GameRoomStatus.FINISH:
                    exitData.message += $" {(u.UID == breaker.UID ? 'L' : 'W')}";
                    packet = new Packet().Pack(PacketType.ROOM_CONTROL, exitData);
                    u.Send(packet);
                    break;
            }
        });

        userList.Clear();
        syncVarDataDict.Clear();

        MyDebug.Log($"[room{GameRoomID}] room breaked by {breaker.UID}, Endstate: {status}");

        return true;
    }

    public void StartGame()
    {
        ReadyId = userList[0].UID;
        status = GameRoomStatus.RUNNING;

        // send to users
        var startPackets = new Packet[2];
        var startData = new MessagePacket
        {
            senderID = 0,
            message = $"START/ {ReadyId} {userList[1].UID} {usingDeck[0]} {100}"
            // 선턴ID 상대ID 내덱 상대덱장수
            // TODO: 서버에서도 덱 파싱이 가능하도록 해야됨
        };
        startPackets[0] = new Packet().Pack(PacketType.ROOM_CONTROL, startData);
        startData.message = $"START/ {ReadyId} {userList[0].UID} {usingDeck[1]} {100}";
        startPackets[1] = new Packet().Pack(PacketType.ROOM_CONTROL, startData);
        for (int i = 0; i < 2; ++i)
        {
            var targetUser = userList[i];
            targetUser.AddOnReceivedDelegate(ReceiveRoomOpponent, "RoomOpponent");
            targetUser.AddOnReceivedDelegate(ReceiveRoomBroadcast, "RoomBroadcast");
            targetUser.Send(startPackets[i]);
        }
    }

    public void EndGame(UserToken loser)
    {
        var winner = userList.Find(u => u != loser);
        if (RoomStatus == GameRoomStatus.RUNNING)
        {
            status = GameRoomStatus.FINISH;
            var loserDict = new Dictionary<string, object>();
            var winnerDict = new Dictionary<string, object>();

            if (hsPlayerUid == 0) // hs한 플레이어 없음
            {
                loserDict.Add("lose", (uint)loser.UserData["lose"] + 1);
                winnerDict.Add("win", (uint)winner.UserData["win"] + 1);
                loserDict.Add("moneyPoint", (uint)loser.UserData["moneyPoint"] + 1);
                winnerDict.Add("moneyPoint", (uint)winner.UserData["moneyPoint"] + 3);
            }
            else // hs함
            {
                bool winnerWithHs = winner.UID == hsPlayerUid;
                string loserDictKey = !winnerWithHs ? "honorLose" : "lose";
                string winnerDictKey = winnerWithHs ? "honorWin" : "win";

                loserDict.Add(loserDictKey, (uint)loser.UserData[loserDictKey] + 1);
                winnerDict.Add(winnerDictKey, (uint)winner.UserData[winnerDictKey] + 1);
                loserDict.Add("moneyPoint", (uint)loser.UserData["moneyPoint"] + 1);
                winnerDict.Add("moneyPoint", (uint)winner.UserData["moneyPoint"] + 3);
                if (winnerWithHs)
                {
                    winnerDict.Add("honorPoint", (uint)winner.UserData["honorPoint"] + 3);
                }
            }

            DatabaseManager.Inst.UpdateUser(loser.UID, loserDict);
            loser.UpdateUserData(loserDict);
            DatabaseManager.Inst.UpdateUser(winner.UID, winnerDict);
            winner.UpdateUserData(winnerDict);
        }

        BreakRoom(loser);
    }

    private void ReceiveRoomOpponent(UserToken u, Packet p)
    {
        if (p.Type != (short)PacketType.ROOM_OPPONENT &&
            p.Type != (short)PacketType.ROOM_OPPO_STONEACTION) return;

        var target = GetOpponentUser(u);
        target.Send(p);
    }

    private void ReceiveRoomBroadcast(UserToken u, Packet p)
    {
        if (p.Type != (short)PacketType.ROOM_BROADCAST) return;

        var msg = MessagePacket.Deserialize(p.Data);
        var msg_sender = new MessagePacket(msg);
        var msg_receiver = new MessagePacket(msg);

        // turn end logic
        if (msg.message.StartsWith("TURNEND/"))
        {
            // { ROOM_BROADCAST | networkId | TURNEND/ nextTotalTurn stonePosition localnextturnState OpppNextTurnState }
            var msgArr = msg.message.Split(' ');

            // { ROOM_BROADCAST | 0 | TURNEND / nextTotalTurn stonePosition LocalNextTurnState oppo(임시) }
            msg_sender.senderID = 0;
            msg_sender.message = $"TURNEND/ {msgArr[1]} {msgArr[2]} {msgArr[3]} {msgArr[4]}";
            msg_receiver.senderID = 0;
            msg_receiver.message = $"TURNEND/ {msgArr[1]} {msgArr[2]} {msgArr[4]} {msgArr[3]}";
        }

        // hs player set
        if (msg.message.StartsWith("HS/"))
        {
            var msgArr = msg.message.Split(' '); // HS/ (HSplayerUid)
            var hsPlayer = uint.Parse(msgArr[1]);

            if (hsPlayerUid == 0)
            {
                hsPlayerUid = hsPlayer;
            }
            else if (hsPlayerUid != hsPlayer)
            {
                // problem happened
            }
        }

        if (u.UID == userList[0].UID)
        {
            userList[0].Send(new Packet().Pack(PacketType.ROOM_BROADCAST, msg_sender));
            userList[1].Send(new Packet().Pack(PacketType.ROOM_BROADCAST, msg_receiver));
        }
        else
        {
            userList[1].Send(new Packet().Pack(PacketType.ROOM_BROADCAST, msg_sender));
            userList[0].Send(new Packet().Pack(PacketType.ROOM_BROADCAST, msg_receiver));
        }
    }

    public UserToken GetOpponentUser(UserToken u)
    {
        if (status == GameRoomStatus.INIT) return null;
        if (!userList.Contains(u)) return null;

        if (userList[0].Equals(u)) return userList[1];
        else return userList[0];
    }

    public void InitSyncVar(UserToken sender, SyncVarPacket packet)
    {
        var netID = packet.NetID;
        if (syncVarDataDict.ContainsKey(netID)) return; // Todo: 패킷 보낸 클라이언트에게 SyncVar 값 다시 전송해야함

        syncVarDataDict[netID] = packet.Data;
    }

    public void ChangeSyncVar(UserToken sender, SyncVarPacket packet)
    {
        var netID = packet.NetID;
        if (!syncVarDataDict.ContainsKey(netID)) return;

        syncVarDataDict[netID] = packet.Data;

        foreach (var user in userList)
        {
            if (user == sender) continue;

            var sendPacket = new Packet().Pack(PacketType.SYNCVAR_CHANGE, packet);
            user.Send(sendPacket);
        }
    }
}
