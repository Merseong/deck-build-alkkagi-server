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
        public List<User> userList;

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
            Console.WriteLine("Room Created with " + user1.UID + ", " + user2.UID);
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
