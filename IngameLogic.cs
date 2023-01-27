﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
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
        private int gameRoomID;
        public int GameRoomID => gameRoomID;
        private GameRoomStatus status;
        public GameRoomStatus RoomStatus => status;
        public int ReadyId = -1;

        private List<User> userList = new List<User>(2);
        private Dictionary<uint, byte[]> syncVarDataDict = new Dictionary<uint, byte[]>();

        public GameRoom(int roomId)
        {
            gameRoomID = roomId;
            status = GameRoomStatus.INIT;

            Console.WriteLine($"[MAIN] Room Created, room ID : {roomId}");
        }

        public bool UserEnter(User user)
        {
            // max player
            if (userList.Count > 2) return false;

            // 방 생성 초기에만 입장 가능
            if (status != GameRoomStatus.INIT) return false;

            userList.Add(user);
            user.Room = this;
            ServerManager.Inst.ApplyRoomIncluded(user);

            var toSend = new MessagePacket();
            toSend.senderID = 0;
            toSend.message = $"{gameRoomID} ENTERED";
            var packet = new Packet().Pack(PacketType.ROOM_CONTROL, toSend);
            user.UserToken.Send(packet);

            Console.WriteLine($"[room{gameRoomID}] {user.UID} entered");

            if (userList.Count == 2)
            {
                status = GameRoomStatus.READY;
                // READY상태에서 양 클라이언트의 로딩이 완료되면 그때 RUNNING으로 넘겨야 함
            }

            return true;
        }

        public bool BreakRoom()
        {
            userList.ForEach(u =>
            {
                u.Room = null;
                switch (status)
                {
                    case GameRoomStatus.INIT:
                        // TODO: 그냥 방폭
                        break;
                    case GameRoomStatus.READY:
                        // TODO: 그냥 방폭
                        break;
                    case GameRoomStatus.RUNNING:
                        // TODO: 추가 패킷을 통해 룸에서 나가졌다고 얘기해야함
                        // 나간 쪽의 패배 선언을 보내야 함
                        break;
                    case GameRoomStatus.FINISH:
                        // TODO: 적당히 방폭
                        break;
                    default:
                        break;
                }
            });

            return true;
        }

        public void StartGame()
        {
            ReadyId = userList[0].UID;
            status = GameRoomStatus.RUNNING;

            var startData = new MessagePacket();
            startData.senderID = 0;
            startData.message = $"{ReadyId} START";
            var startPacket = new Packet().Pack(PacketType.ROOM_CONTROL, startData);
            userList.ForEach(u => 
            {
                u.UserToken.ProcessPacket += ReceiveRoomOpponent;

                u.UserToken.Send(startPacket);
            });
        }

        private void ReceiveRoomOpponent(User u, Packet p)
        {
            if (p.Type != (short)PacketType.ROOM_OPPONENT) return;

            var target = GetOpponentUser(u);
            target.UserToken.Send(p);
        }

        public User GetOpponentUser(User u)
        {
            if (status == GameRoomStatus.INIT) return null;
            if (!userList.Contains(u)) return null;

            if (userList[0].Equals(u)) return userList[1];
            else return userList[0];
        }

        public void InitSyncVar(User sender, SyncVarPacket packet)
        {
            var netID = packet.NetID;
            if (syncVarDataDict.ContainsKey(netID)) return; // Todo: 패킷 보낸 클라이언트에게 SyncVar 값 다시 전송해야함

            syncVarDataDict[netID] = packet.Data;
        }

        public void ChangeSyncVar(User sender, SyncVarPacket packet)
        {
            var netID = packet.NetID;
            if (!syncVarDataDict.ContainsKey(netID)) return;

            syncVarDataDict[netID] = packet.Data;

            foreach (var user in userList)
            {
                if (user == sender) continue;

                var sendPacket = new Packet();
                sendPacket.Type = (Int16)PacketType.SYNCVAR_CHANGE;
                var data = packet.Serialize();
                sendPacket.SetData(data, data.Length);
                user.UserToken.Send(sendPacket);
            }
        }
    }
}
