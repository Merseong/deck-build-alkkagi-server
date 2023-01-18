using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class ServerManager : Singleton<ServerManager>
    {
        public bool threadLive { get; set; }
        private int nextID = 1;
        private int nextRoomID = 1;
        public int NextId { get { return nextID++; } }
        public int NextRoomID { get { return nextRoomID++; } }

        private HashSet<User> userList;
        public HashSet<User> removedUserListBuffer; // Socket Close전에 버퍼에 담아 한번에 처리
        private HashSet<GameRoom> gameRoomList;

        public User waitingUser = null;

        protected override void Init()
        {
            userList = new HashSet<User>();
            removedUserListBuffer = new HashSet<User>();
            gameRoomList = new HashSet<GameRoom>();
            threadLive = true;
        }

        public GameRoom EnterGameRoom(User user)
        {
            if (waitingUser == null)
            {
                waitingUser = user;
                return null;
            }
            else
            {
                var newRoom = new GameRoom(waitingUser, user);
                gameRoomList.Add(newRoom);

                waitingUser = null;

                return newRoom;
            }
        }

        public void OnNewClient(Socket clientSocket, object eventArgs)
        {
            // UserToken은 유저 연결 시 해당 소켓 저장 및 메세지 송수신 기능
            UserToken token = new UserToken(clientSocket);

            // User는 db에서 가져온 데이터 저장하는 객체, 유저의 정보 보유
            User user = new User(token);
            token.User = user;

            token.StartReceive();

            // Add user to userList
            userList.Add(user);
            Console.WriteLine($"User counter: {userList.Count()}");
        }

        public void DoUpdate()
        {
            while (threadLive)
            {
                ProcessPacket();
                DeleteUser();
                Thread.Sleep(100);
            }
        }

        private void ProcessPacket()
        {
            foreach (var user in userList)
            {
                user.UserToken.Update();
            }
        }

        private void DeleteUser()
        {
            foreach (var user in removedUserListBuffer)
            {
                user.UserToken.Close();
                userList.Remove(user);
                Console.WriteLine($"User counter: {userList.Count()}");
            }

            removedUserListBuffer.Clear();
        }

        public void Notify(Packet packet, User sender)
        {
            foreach (var user in userList)
            {
                if (user == sender) continue;

                user.UserToken.Send(packet);
            }
        }
    }
}
