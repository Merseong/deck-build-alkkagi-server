using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class Listener
    {
        SocketAsyncEventArgs acceptArgs;
        Socket listenSocket;
        AutoResetEvent flowControlEvent;
        bool threadLive { get; set; }

        public delegate void NewClientHandler(Socket clientSocket, object token);
        public NewClientHandler callbackOnNewClient;

        public Listener()
        {
            callbackOnNewClient = null;
            threadLive = true;
        }

        public void StartServer(string host, int port, int backlog)
        {
            // Open socket    
            listenSocket = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            listenSocket.NoDelay = true;

            IPAddress address;

            if (host == "0.0.0.0")
                address = IPAddress.Any;
            else
                address = IPAddress.Parse(host);

            IPEndPoint endPoint = new IPEndPoint(address, port);

            try
            {
                listenSocket.Bind(endPoint);
                listenSocket.Listen(backlog);

                acceptArgs = new SocketAsyncEventArgs();
                acceptArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);

                // 별도의 스레드에서
                Thread listenThread = new Thread(DoListen);
                listenThread.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private void DoListen()
        {
            flowControlEvent = new AutoResetEvent(false);
            Console.WriteLine("Thread is running");

            while (threadLive)
            {
                acceptArgs.AcceptSocket = null;
                bool pending = true;

                try
                {
                    pending = listenSocket.AcceptAsync(acceptArgs);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                if (!pending)
                    OnAcceptCompleted(null, acceptArgs);

                flowControlEvent.WaitOne();
            }
        }

        private void OnAcceptCompleted(object senderm, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Socket clientSocket = e.AcceptSocket;

                ServerManager.Inst.OnNewClient(clientSocket, e);

                flowControlEvent.Set();
            }
            else
            {
            }
        }

        public void StopServer()
        {
            listenSocket.Close();
            threadLive = false;
            ServerManager.Inst.MatchQueue.ThreadLive = false;
        }
    }
}
