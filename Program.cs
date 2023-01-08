using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace alkkagi_server
{
    static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }

    public class myServer
    {
        // Get help from https://shakddoo.tistory.com/
        public class myPacket
        {
            public Int16 m_type { get; set; }
            public byte[] m_data { get; set; }

            public myPacket() { }

            public void SetData(byte[] data, int len)
            {
                m_data = new byte[len];
                Array.Copy(data, m_data, len);
            }

            public byte[] GetSendBytes()
            {
                byte[] type_bytes = BitConverter.GetBytes(m_type);
                int header_size = (int)(m_data.Length);
                byte[] header_bytes = BitConverter.GetBytes(header_size);
                // 헤더 + 패킷 타입 + 데이터(객체의 직렬화)
                byte[] send_bytes = new byte[header_bytes.Length + type_bytes.Length + m_data.Length];

                // 헤더 복사 0 ~ header len
                Array.Copy(header_bytes, 0, send_bytes, 0, header_bytes.Length);

                // 타입 복사 header len ~ header len + type len 
                Array.Copy(type_bytes, 0, send_bytes, header_bytes.Length, type_bytes.Length);

                // 데이터 복사
                Array.Copy(m_data, 0, send_bytes, header_bytes.Length + type_bytes.Length, m_data.Length);

                return send_bytes;
            }
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 1)] // 1byte 단위
        public class myData<T> where T : class
        {
            public myData() { }

            public byte[] Serialize()
            {
                var size = Marshal.SizeOf(typeof(T));
                var array = new byte[size];
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(this, ptr, true);
                Marshal.Copy(ptr, array, 0, size);
                Marshal.FreeHGlobal(ptr);
                return array;
            }

            public static T Deserialize(byte[] array)
            {
                var size = Marshal.SizeOf(typeof(T));
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(array, 0, ptr, size);
                var s = (T)Marshal.PtrToStructure(ptr, typeof(T));
                Marshal.FreeHGlobal(ptr);
                return s;
            }
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class TestPacket : myData<TestPacket>
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string m_message;

            public TestPacket() { }
        }

        public class myListener
        {
            SocketAsyncEventArgs m_accept_args;
            Socket m_listen_socket;
            AutoResetEvent m_flow_control_event;
            bool m_thread_live { get; set; }

            public delegate void NewClientHandler(Socket client_socket, object token);
            public NewClientHandler m_callback_on_new_client;

            public myListener()
            {
                m_callback_on_new_client = null;
                m_thread_live = true;
            }

            public void StartServer(string host, int port, int backlog)
            {
                m_listen_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                m_listen_socket.NoDelay = true;

                IPAddress address;

                if (host == "0.0.0.0")
                {
                    address = IPAddress.Any;
                }
                else
                {
                    address = IPAddress.Parse(host);
                }
                IPEndPoint endPoint = new IPEndPoint(address, port);

                try
                {
                    m_listen_socket.Bind(endPoint);
                    m_listen_socket.Listen(backlog);

                    m_accept_args = new SocketAsyncEventArgs();
                    m_accept_args.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);

                    Thread listen_thread = new Thread(DoListen);
                    m_thread_live = true;
                    listen_thread.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            void DoListen()
            {
                m_flow_control_event = new AutoResetEvent(false);

                while (m_thread_live)
                {
                    m_accept_args.AcceptSocket = null;
                    bool pending = true;

                    try
                    {
                        pending = m_listen_socket.AcceptAsync(m_accept_args);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        continue;
                    }

                    if (!pending)
                    {
                        OnAcceptCompleted(null, m_accept_args);
                    }

                    m_flow_control_event.WaitOne();
                }
            }

            void OnAcceptCompleted(object sender, SocketAsyncEventArgs se)
            {
                if (se.SocketError == SocketError.Success)
                {
                    Socket client_socket = se.AcceptSocket;

                    Console.WriteLine(client_socket.ToString());

                    try
                    {
                        if (client_socket != null)
                        {
                            client_socket.Shutdown(SocketShutdown.Both);
                        } 
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        if (client_socket != null)
                        {
                            client_socket.Close();
                        }
                    }
                }
                else
                {
                    // 연결 실패
                }

                m_flow_control_event.Set();
            }

            public void Close()
            {
                m_listen_socket.Close();
                m_thread_live = false;
            }
        }
    }
}
