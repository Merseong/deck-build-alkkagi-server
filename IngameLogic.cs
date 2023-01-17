using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class ServerManager : Singleton<ServerManager>
    {
        public int m_nextUid = 1;
        public int m_nextRoomId = 1;

        HashSet<User> m_userList;
        HashSet<GameRoom> m_gameRoomList;

        public User m_waiting = null;

        protected override void Init()
        {
            // 이후 룸 만들고 지우는 작업들을 다 해야됨 -> 풀링?
            m_userList = new HashSet<User>();
            m_gameRoomList = new HashSet<GameRoom>();
        }

        public User AddUser(UserToken userToken)
        {
            var newUser = new User(userToken);
            m_userList.Add(newUser);

            return newUser;
        }

        public GameRoom EnterGameRoom(User user)
        {
            if (m_waiting == null)
            {
                m_waiting = user;
                return null;
            }
            else
            {
                var newRoom = new GameRoom(m_waiting, user);
                m_gameRoomList.Add(newRoom);

                m_waiting = null;

                return newRoom;
            }
        }
    }


    public class GameRoom
    {
        // 게임 룸
        public int m_gameRoomId;
        public List<User> m_userList;

        public GameRoom(User user1, User user2)
        {
            m_gameRoomId = ServerManager.Inst.m_nextRoomId++;
            m_userList = new List<User>()
            {
                user1,
                user2
            };
            user1.m_room = this;
            user2.m_room = this;
            Console.WriteLine("Room Created with " + user1.m_uid + ", " + user2.m_uid);
        }

        public void SendToOpponent(int sender, string message)
        {
            var toSend = new MessagePacket();
            toSend.m_senderid = sender;
            toSend.m_message = message;
            var sendPacket = new Packet();
            sendPacket.m_type = (short)PacketType.ROOM_OPPONENT;
            var toSendSerial = toSend.Serialize();
            sendPacket.SetData(toSendSerial, toSendSerial.Length);
            m_userList.Find(e => e.m_uid != sender).m_token.Send(sendPacket);
        }
    }
}
