using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class NetworkManager : Singleton<NetworkManager>
    {
        List<User> userList;
        public List<User> userListBuffer;
        public bool threadLive { get; set; }

        protected override void Init()
        {
            userList = new List<User>();
            userListBuffer = new List<User>();
            threadLive = true;
        }

        public void OnNewClient(Socket clientSocket, object eventArgs)
        {
            // UserToken은 유저 연결 시 해당 소켓 저장 및 메세지 송수신 기능
            UserToken token = new UserToken();
            token.Init();

            // User는 db에서 가져온 데이터 저장하는 객체, 유저의 정보 보유
            User user = new User(token);
            token.User = user;

            token.Socket = clientSocket;
            token.Socket.NoDelay = true;
            token.Socket.ReceiveTimeout = 60 * 1000;
            token.Socket.SendTimeout = 60 * 1000;

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
                user.m_token.Update();
            }
        }

        private void DeleteUser()
        {
            foreach (var user in userListBuffer)
            {
                user.m_token.Close();
                userList.Remove(user);
                Console.WriteLine($"User counter: {userList.Count()}");
            }

            userListBuffer.Clear();
        }

        public void Notify(Packet packet, User sender)
        {
            foreach (var user in userList)
            {
                if (user == sender) continue;

                user.m_token.Send(packet);
            }
        }
    }
}
