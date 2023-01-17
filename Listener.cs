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
        SocketAsyncEventArgs mAcceptArgs;
        Socket mListenSocket;
        AutoResetEvent mFlowControlEvent;
        bool mThreadLive { get; set; }

        public delegate void NewClientHandler(Socket clientSocket, object token);
        public NewClientHandler mCallbackOnNewClient;

        public Listener()
        {
            mCallbackOnNewClient = null;
            mThreadLive = true;
        }

        public void StartServer(string host, int port, int backlog)
        {
            // Open socket    
            mListenSocket = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            mListenSocket.NoDelay = true;

            IPAddress address;

            if (host == "0.0.0.0")
                address = IPAddress.Any;
            else
                address = IPAddress.Parse(host);

            IPEndPoint endPoint = new IPEndPoint(address, port);

            try
            {
                mListenSocket.Bind(endPoint);
                mListenSocket.Listen(backlog);

                mAcceptArgs = new SocketAsyncEventArgs();
                mAcceptArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);

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
            mFlowControlEvent = new AutoResetEvent(false);
            Console.WriteLine("Thread is running");

            while (mThreadLive)
            {
                mAcceptArgs.AcceptSocket = null;
                bool pending = true;

                try
                {
                    pending = mListenSocket.AcceptAsync(mAcceptArgs);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                if (!pending)
                    OnAcceptCompleted(null, mAcceptArgs);

                mFlowControlEvent.WaitOne();
            }
        }

        private void OnAcceptCompleted(object senderm, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Socket clientSocket = e.AcceptSocket;

                NetworkManager.Inst.OnNewClient(clientSocket, e);

                mFlowControlEvent.Set();
            }
            else
            {
            }
        }

        public void Close()
        {
            mListenSocket.Close();
        }
    }
}
