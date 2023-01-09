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

    public class MyServer
    {
        // Get help from https://shakddoo.tistory.com/
        public class Packet
        {
            public Int16 m_type { get; set; }
            public byte[] m_data { get; set; }

            public Packet() { }

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
        public class Data<T> where T : class
        {
            public Data() { }

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
        public class TestPacket : Data<TestPacket>
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string m_message;

            public TestPacket() { }
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class MessagePacket : Data<MessagePacket>
        {
            public int m_senderid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string m_message;

            public MessagePacket() { }
        }


        public enum PacketType
        {
            UNDEFINED,
            PACKET_USER_CLOSED,
            TEST_PACKET,
            ROOM_BROADCAST,
            ROOM_OPPONENT,
            ROOM_ENTER,
            PACKET_COUNT
        }

        public class MessageResolver
        {
            public delegate void CompletedMessageCallback(Packet packet);

            int m_message_size;
            byte[] m_message_buffer = new byte[1024 * 2000];
            byte[] m_header_buffer = new byte[4];
            byte[] m_type_buffer = new byte[2];

            PacketType m_pre_type;

            int m_head_position;
            int m_type_position;
            int m_current_position;

            short m_message_type;
            int m_remain_bytes;

            bool m_head_completed;
            bool m_type_completed;
            bool m_completed;

            CompletedMessageCallback m_completed_callback;

            public MessageResolver()
            {
                ClearBuffer();
            }

            public void ClearBuffer()
            {
                Array.Clear(m_message_buffer, 0, m_message_buffer.Length);
                Array.Clear(m_header_buffer, 0, m_header_buffer.Length);
                Array.Clear(m_type_buffer, 0, m_type_buffer.Length);

                m_message_size = 0;
                m_head_position = 0;
                m_type_position = 0;
                m_current_position = 0;
                m_message_type = 0;

                m_head_completed = false;
                m_type_completed = false;
                m_completed = false;
            }

            public void OnReceive(byte[] buffer, int offset, int transferred, CompletedMessageCallback callback)
            {
                int src_position = offset; // 현재 들어온 데이터의 위치
                m_completed_callback = callback; // 메세지가 완성된 경우 호출하는 콜백
                m_remain_bytes = transferred; // 남은 처리할 메세지 양

                if (!m_head_completed)
                {
                    m_head_completed = readHead(buffer, ref src_position);

                    if (!m_head_completed)
                        return;

                    m_message_size = getBodySize();

                    if (m_message_size < 0 || m_message_size > 1024 * 2000)
                    {
                        return;
                    }
                }

                if (!m_type_completed)
                {
                    //남은 데이터가 있다면, 타입 정보를 완성한다.
                    m_type_completed = readType(buffer, ref src_position);

                    //타입 정보를 완성하지 못했다면, 다음 메시지 전송을 기다린다.
                    if (!m_type_completed)
                        return;

                    //타입 정보를 완성했다면, 패킷 타입을 정의한다. (enum type)
                    m_message_type = BitConverter.ToInt16(m_type_buffer, 0);


                    //잘못된 데이터인지 확인
                    if (m_message_type < 0 ||
                       m_message_type > (int)PacketType.PACKET_COUNT - 1)
                    {
                        return;
                    }

                    //데이터가 미완성일 경우, 다음에 전송되었을 때를 위해 저장해 둔다.
                    m_pre_type = (PacketType)m_message_type;
                }


                if (!m_completed)
                {
                    //남은 데이터가 있다면, 데이터 완성과정을 진행한다.
                    m_completed = readBody(buffer, ref src_position);
                    if (!m_completed)
                        return;
                }

                //데이터가 완성 되었다면, 패킷으로 만든다.
                Packet packet = new Packet();
                packet.m_type = m_message_type;
                packet.SetData(m_message_buffer, m_message_size);

                //패킷이 완성 되었음을 알린다.
                m_completed_callback(packet);

                //패킷을 만드는데, 사용한 버퍼를 초기화 해준다.
                ClearBuffer();
            }

            private bool readHead(byte[] buffer, ref int src_position)
            {
                return readUntil(buffer, ref src_position, m_header_buffer, ref m_head_position, 4);
            }

            private bool readType(byte[] buffer, ref int src_position)
            {
                return readUntil(buffer, ref src_position, m_type_buffer, ref m_type_position, 2);
            }

            private bool readBody(byte[] buffer, ref int src_position)
            {
                return readUntil(buffer, ref src_position, m_message_buffer, ref m_current_position, m_message_size);
            }

            bool readUntil(byte[] buffer, ref int src_position, byte[] dest_buffer, ref int dest_position, int to_size)
            {
                //남은 데이터가 없다면, 리턴
                if (m_remain_bytes < 0)
                    return false;

                int copy_size = to_size - dest_position;
                if (m_remain_bytes < copy_size)
                    copy_size = m_remain_bytes;

                Array.Copy(buffer, src_position, dest_buffer, dest_position, copy_size);

                //시작 위치를 옮겨준다.
                src_position += copy_size;
                dest_position += copy_size;
                m_remain_bytes -= copy_size;

                return !(dest_position < to_size);
            }

            int getBodySize()
            {
                Type type = ((Int16)1).GetType();
                if (type.Equals(typeof(Int16)))
                {
                    return BitConverter.ToInt16(m_header_buffer, 0);
                }

                return BitConverter.ToInt32(m_header_buffer, 0);
            }
        }

        public class MyListener
        {
            SocketAsyncEventArgs m_accept_args;
            Socket m_listen_socket;
            AutoResetEvent m_flow_control_event;
            bool m_thread_live { get; set; }

            public delegate void NewClientHandler(Socket client_socket, object token);
            public NewClientHandler m_callback_on_new_client;

            public MyListener()
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

                    OnNewClient(client_socket, se);
                }
                else
                {
                    // 연결 실패
                }

                m_flow_control_event.Set();
            }

            private void OnNewClient(Socket client_socket, object e)
            {
                Console.WriteLine(client_socket.AddressFamily);

                UserToken token = new UserToken(client_socket);

                token.m_socket.NoDelay = true;
                token.m_socket.ReceiveTimeout = 60 * 1000;
                token.m_socket.SendTimeout = 60 * 1000;

                token.StartReceive();
            }

            public void Close()
            {
                m_listen_socket.Close();
                m_thread_live = false;
            }
        }

        public class UserToken
        {
            User m_user;
            SocketAsyncEventArgs m_receive_event_args;
            MessageResolver m_message_resolver;
            public Socket m_socket;

            List<Packet> m_packet_list = new List<Packet>(5);
            object m_mutex_packet_list = new object();

            SocketAsyncEventArgs m_send_event_args;

            Queue<Packet> m_send_packet_queue = new Queue<Packet>(100);
            object m_mutex_send_list = new object();

            public UserToken(Socket socket)
            {
                m_user = ServerManager.Instance.AddUser(this);
                m_socket = socket;

                m_message_resolver = new MessageResolver();

                m_receive_event_args = new SocketAsyncEventArgs();
                m_receive_event_args.Completed += OnReceiveCompleted;
                m_receive_event_args.UserToken = this;

                m_send_event_args = new SocketAsyncEventArgs();
                m_send_event_args.Completed += OnSendCompleted;
                m_send_event_args.UserToken = this;

                //byte배열을 크게 미리 세팅하고, 그 배열을 재사용한다.
                //args.SetBuffer(m_buffer, m_current_index, m_buffer_size);
                //아래 따로 코드 첨부
                BufferManager.Instance.SetBuffer(m_receive_event_args);
                BufferManager.Instance.SetBuffer(m_send_event_args);
            }

            public void StartReceive()
            {
                bool pending = m_socket.ReceiveAsync(m_receive_event_args);
                if (!pending)
                {
                    OnReceiveCompleted(this, m_receive_event_args);
                }
            }

            public void Send(Packet packet)
            {
                if (m_socket == null)
                {
                    return;
                }

                lock (m_mutex_send_list)
                {
                    //수신 중인 패킷이 없으면 바로, 전송
                    if (m_send_packet_queue.Count < 1)
                    {
                        m_send_packet_queue.Enqueue(packet);
                        SendProcess();
                        return;
                    }

                    //수신 중인 패킷이 있으면, 큐에 넣고 나감.
                    //쌓인 패킷이 100개가 넘으면 그 다음부터는 무시함. 제 겜은 그래도 됨..        
                    if (m_send_packet_queue.Count < 100)
                        m_send_packet_queue.Enqueue(packet);
                }
            }

            void SendProcess()
            {
                if (m_socket == null)
                {
                    return;
                }

                Packet packet = m_send_packet_queue.Peek();
                byte[] send_data = packet.GetSendBytes();

                int data_len = send_data.Length;

                if (data_len > 4096)
                {
                    SocketAsyncEventArgs send_event_args = new SocketAsyncEventArgs();
                    if (send_event_args == null)
                    {
                        Console.WriteLine("SocketAsyncEventArgsPool::Pop() result is null");
                        return;
                    }

                    send_event_args.Completed += OnSendCompletedPooling;
                    send_event_args.UserToken = this;
                    send_event_args.SetBuffer(send_data, 0, send_data.Length);

                    bool pending = m_socket.SendAsync(send_event_args);
                    if (!pending)
                        OnSendCompletedPooling(null, send_event_args);
                }
                else
                {
                    //버퍼풀에서 설정한 크기보다 작은 경우(4K 이하)

                    //버퍼를 설정
                    m_send_event_args.SetBuffer(m_send_event_args.Offset, send_data.Length);
                    //버퍼에 데이터 복사
                    Array.Copy(send_data, 0, m_send_event_args.Buffer, m_send_event_args.Offset, send_data.Length);

                    bool pending = m_socket.SendAsync(m_send_event_args);
                    if (!pending)
                        OnSendCompleted(null, m_send_event_args);
                }
            }

            void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
            {
                if (e.SocketError != SocketError.Success)
                {
                    Console.WriteLine(e.SocketError.ToString());
                    Packet packet = new Packet();
                    packet.m_type = (Int16)PacketType.PACKET_USER_CLOSED;

                    AddPacket(packet);
                }

                if (e.BytesTransferred > 0)
                {
                    m_message_resolver.OnReceive(e.Buffer, e.Offset, e.BytesTransferred, OnMessageCompleted);

                    StartReceive();
                }
                else
                {
                    Console.WriteLine("SocketError.Success, but BytesTransferred is 0");
                    Packet packet = new Packet();
                    packet.m_type = (Int16)PacketType.PACKET_USER_CLOSED;

                    AddPacket(packet);
                }
            }

            void OnMessageCompleted(Packet packet)
            {
                AddPacket(packet);
            }

            void OnSendCompleted(object sender, SocketAsyncEventArgs e)
            {
                if (e.SocketError == SocketError.Success)
                {
                    lock (m_mutex_send_list)
                    {
                        if (m_send_packet_queue.Count > 0)
                            m_send_packet_queue.Dequeue();

                        if (m_send_packet_queue.Count > 0)
                            SendProcess();
                    }
                }
            }

            void OnSendCompletedPooling(object sender, SocketAsyncEventArgs e)
            {
                /*
                if (e.BufferList != null)
                {
                    e.BufferList = null;
                }
                e.SetBuffer(null, 0, 0);
                e.UserToken = null;
                e.RemoteEndPoint = null;

                e.Completed -= onSendCompletedPooling;
                SocketAsyncEventArgsPool.Instance.Push(e);
                */


                if (e.SocketError == SocketError.Success)
                {
                    //LogManager.Debug("onSendComplected Thread ID: " + Thread.CurrentThread.ManagedThreadId);
                    lock (m_mutex_send_list)
                    {
                        if (m_send_packet_queue.Count > 0)
                            m_send_packet_queue.Dequeue();

                        if (m_send_packet_queue.Count > 0)
                            SendProcess();
                    }
                }
                else
                {
                }
            }

            public void AddPacket(Packet packet)
            {
                lock (m_mutex_packet_list)
                {
                    m_packet_list.Add(packet);
                }
                Update();
            }

            public void Update()
            {
                if (m_packet_list.Count > 0)
                {
                    lock (m_mutex_packet_list)
                    {
                        try
                        {
                            foreach (Packet packet in m_packet_list)
                            {
                                m_user.ProcessPacket(packet);
                            }
                            m_packet_list.Clear();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }
            }

            public void Close()
            {
                try
                {
                    if (m_socket != null)
                    {
                        m_socket.Shutdown(SocketShutdown.Both);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    if (m_socket != null)
                    {
                        m_socket.Close();
                    }
                    Console.WriteLine("socket closed");
                }

                m_socket = null;
                m_user = null;
                m_message_resolver.ClearBuffer();

                lock (m_mutex_packet_list)
                {
                    m_packet_list.Clear();
                }

                lock (m_mutex_send_list)
                {
                    m_send_packet_queue.Clear();
                }

                //수신 객체 해제
                {
                    BufferManager.Instance.FreeBuffer(m_receive_event_args);
                    m_receive_event_args.SetBuffer(null, 0, 0);
                    if (m_receive_event_args.BufferList != null)
                        m_receive_event_args.BufferList = null;

                    m_receive_event_args.UserToken = null;
                    m_receive_event_args.RemoteEndPoint = null;

                    m_receive_event_args.Completed -= OnReceiveCompleted;


                    m_send_event_args.Dispose();
                    //풀링하지 않는 경우엔 반드시, m_send_event_args.Dispose();


                    m_receive_event_args = null;
                }


                //송신 객체 해제
                {
                    BufferManager.Instance.FreeBuffer(m_send_event_args);

                    if (m_send_event_args.BufferList != null)
                        m_send_event_args.BufferList = null;

                    m_send_event_args.UserToken = null;
                    m_send_event_args.RemoteEndPoint = null;

                    m_send_event_args.Completed -= OnSendCompleted;


                    m_send_event_args.Dispose();
                    //풀링하지 않는 경우엔 반드시, m_send_event_args.Dispose();


                    m_send_event_args = null;
                }

            }
        }

        public class BufferManager : Singleton<BufferManager>
        {
            int m_num_bytes;                // the total number of bytes controlled by the buffer pool
            byte[] m_buffer;                   // the underlying byte array maintained by the Buffer Manager
            Stack<int> m_free_index_pool;
            int m_current_index;
            int m_buffer_size;

            public BufferManager()
            {
                
            }

            protected override void Init()
            {
                //최대 유저 수 x2(송신용,수신용)
                m_num_bytes = 10 * 4096 * 2; // MaxConnection * SocketBufferSize * 2
                m_current_index = 0;
                m_buffer_size = 4096;
                m_free_index_pool = new Stack<int>();
                m_buffer = new byte[m_num_bytes];
            }

            public bool SetBuffer(SocketAsyncEventArgs args)
            {
                if (m_free_index_pool.Count > 0)
                {
                    args.SetBuffer(m_buffer, m_free_index_pool.Pop(), m_buffer_size);
                }
                else
                {
                    if (m_num_bytes < (m_current_index + m_buffer_size))
                    {
                        return false;
                    }
                    args.SetBuffer(m_buffer, m_current_index, m_buffer_size);
                    m_current_index += m_buffer_size;
                }
                return true;
            }

            /// <summary>
            /// Removes the buffer from a SocketAsyncEventArg object.  This frees the buffer back to the
            /// buffer pool
            /// </summary>
            public void FreeBuffer(SocketAsyncEventArgs args)
            {
                if (args == null)
                    return;
                m_free_index_pool.Push(args.Offset);

                //args.SetBuffer(null, 0, 0); //가끔 SocketAsyncEventArgs에서 사용중이라고 이셉션 발생가능하기 때문에,이 함수 밖에서 처리함.
            }
        }
    }
}

public class Singleton<T> where T : Singleton<T>, new()
{
    static T mInstnace;
    public static T Instance
    {
        get
        {
            if (mInstnace == null)
            {
                mInstnace = new T();
                mInstnace.Init();
            }

            return mInstnace;
        }
    }

    protected virtual void Init()
    {

    }
}
