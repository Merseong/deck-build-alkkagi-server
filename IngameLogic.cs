using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class GameRoom
    {
        // 게임 룸
        public int gameRoomID;
        private List<User> userList;
        private Dictionary<uint, byte[]> syncVarDataDict = new Dictionary<uint, byte[]>();

        public GameRoom(User user1, User user2)
        {
            gameRoomID = ServerManager.Inst.NextRoomID;
            userList = new List<User>()
            {
                user1,
                user2
            };
            user1.Room = this;
            user2.Room = this;

            var packet = new Packet();
            packet.Type = (Int16)PacketType.ROOM_ENTER;
            packet.SetData(new byte[] {0}, 1);
            user1.UserToken.Send(packet);
            user2.UserToken.Send(packet);

            Console.WriteLine("Room Created with " + user1.UID + ", " + user2.UID);
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

        public void SendToOpponent(int sender, string message)
        {
            var toSend = new MessagePacket();
            toSend.senderID = sender;
            toSend.message = message;
            var sendPacket = new Packet();
            sendPacket.Type = (short)PacketType.ROOM_OPPONENT;
            var toSendSerial = toSend.Serialize();
            sendPacket.SetData(toSendSerial, toSendSerial.Length);
            userList.Find(e => e.UID != sender).UserToken.Send(sendPacket);
        }
    }
}
