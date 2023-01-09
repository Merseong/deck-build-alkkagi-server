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

        public User AddUser(MyServer.UserToken userToken)
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

    public class User
    {
        // 유저 정보
        public MyServer.UserToken m_token;
        public int m_uid;
        public GameRoom m_room;

        public User(MyServer.UserToken userToken)
        {
            m_token = userToken;
            m_uid = ServerManager.Instance.m_nextUid++;
        }

        public void ProcessPacket(MyServer.Packet packet)
        {
            if (packet.m_type == (short)MyServer.PacketType.PACKET_USER_CLOSED)
            {
                m_token.Close();
                return;
            }

            var message = "";

            switch ((MyServer.PacketType)packet.m_type)
            {
                case MyServer.PacketType.PACKET_USER_CLOSED:
                    m_token.Close();
                    break;
                case MyServer.PacketType.ROOM_BROADCAST:
                case MyServer.PacketType.ROOM_OPPONENT:
                    message = MyServer.MessagePacket.Deserialize(packet.m_data).m_message;
                    if (m_room != null)
                    {
                        Console.WriteLine(m_uid + ") send message \"" + message + "\"");
                        m_room.SendToOpponent(m_uid, message);
                    }
                    break;
                case MyServer.PacketType.ROOM_ENTER:
                    if (m_room == null)
                    {
                        Console.WriteLine(m_uid + ") Entering room");
                        ServerManager.Instance.EnterGameRoom(this);
                    }
                    break;
                case MyServer.PacketType.TEST_PACKET:
                default:
                    message = MyServer.TestPacket.Deserialize(packet.m_data).m_message;
                    Console.WriteLine(message);
                    m_token.Send(packet);
                    break;
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
            m_gameRoomId = ServerManager.Instance.m_nextRoomId++;
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
            var toSend = new MyServer.MessagePacket();
            toSend.m_senderid = sender;
            toSend.m_message = message;
            var sendPacket = new MyServer.Packet();
            sendPacket.m_type = (short)MyServer.PacketType.ROOM_OPPONENT;
            var toSendSerial = toSend.Serialize();
            sendPacket.SetData(toSendSerial, toSendSerial.Length);
            m_userList.Find(e => e.m_uid != sender).m_token.Send(sendPacket);
        }
    }
}
