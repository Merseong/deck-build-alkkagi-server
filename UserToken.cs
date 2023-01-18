using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class UserToken
    {
        User mUser; // 유저 정보 저장
        public User User { get { return mUser; } set { mUser = value; } }
        SocketAsyncEventArgs mReceiveEventArgs; // 메세지 받을 때 사용
        MessageResolver mMessageResolver;
        Socket mSocket;
        public Socket Socket { get { return mSocket; } set { mSocket = value; } }

        List<Packet> mPacketList = new List<Packet>(5);
        object mMutextPacketList = new object();

        SocketAsyncEventArgs m_send_event_args;

        Queue<Packet> m_send_packet_queue = new Queue<Packet>(100);
        object m_mutex_send_list = new object();

        public UserToken(Socket socket)
        {
            mSocket = socket;
            mMessageResolver = new MessageResolver();

            mReceiveEventArgs = new SocketAsyncEventArgs();
            mReceiveEventArgs.Completed += OnReceiveCompleted;
            mReceiveEventArgs.UserToken = this;

            m_send_event_args = new SocketAsyncEventArgs();
            m_send_event_args.Completed += OnSendCompleted;
            m_send_event_args.UserToken = this;

            //byte배열을 크게 미리 세팅하고, 그 배열을 재사용한다.
            //args.SetBuffer(m_buffer, m_current_index, m_buffer_size);
            //아래 따로 코드 첨부
            BufferManager.Inst.SetBuffer(m_send_event_args);
            BufferManager.Inst.SetBuffer(mReceiveEventArgs);
        }

        public void StartReceive()
        {
            // 패킷 송신 대기
            // 패킷이 오면 OnReceiveCompleted 콜백함수 호출
            // MessageResolver를 통해 바이트 배열을 패킷으로 변환
            bool pending = mSocket.ReceiveAsync(mReceiveEventArgs);
            if (!pending)
                OnReceiveCompleted(this, mReceiveEventArgs);
        }

        public void Send(Packet packet)
        {
            if (mSocket == null)
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

        private void SendProcess()
        {
            if (mSocket == null)
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

                bool pending = mSocket.SendAsync(send_event_args);
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

                bool pending = mSocket.SendAsync(m_send_event_args);
                if (!pending)
                    OnSendCompleted(null, m_send_event_args);
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                // 전송 성공
                mMessageResolver.OnReceive(e.Buffer, e.Offset, e.BytesTransferred, OnMessageCompleted);

                StartReceive();
            }
            else
            {
                Console.WriteLine(e.SocketError.ToString());
                Packet packet = new Packet();
                packet.m_type = (Int16)PacketType.PACKET_USER_CLOSED;

                AddPacket(packet);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
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

            // 나중에 pooling 처리 필요 (메모리 릭 막기 위함)
        }

        private void OnSendCompletedPooling(object sender, SocketAsyncEventArgs e)
        {
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

        private void OnMessageCompleted(Packet packet)
        {
            AddPacket(packet);
        }

        private void AddPacket(Packet packet)
        {
            // 처리 부분과 패킷을 넣는 부분의 스레드가 다르기 때문에 락을 걸음
            lock (mMutextPacketList)
            {
                mPacketList.Add(packet);
            }
            Update();
        }

        //다른 스레드에서 호출된다.
        //때문에 패킷 리스트에 넣고, 처리하기 전에 락을 걸어준다.
        public void Update()
        {

            //완성된 패킷을 매 루프 처리해 준다.
            if (mPacketList.Count > 0)
            {
                lock (mMutextPacketList)
                {
                    try
                    {
                        foreach (Packet packet in mPacketList)
                            mUser.ProcessPacket(packet);
                        mPacketList.Clear();
                    }
                    catch (Exception e)
                    {
                        //잘못된 패킷이 들어온 경우 처리
                        //
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                    }
                }
            }
        }

        // 종료
        public void Close()
        {
            try
            {
                if (mSocket != null)
                    mSocket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                if (mSocket != null)
                    mSocket.Close();
            }

            mSocket = null;
            mUser = null;
            mMessageResolver.ClearBuffer();

            lock (mMutextPacketList)
            {
                mPacketList.Clear();
            }

            lock (m_mutex_send_list)
            {
                m_send_packet_queue.Clear();
            }

            //수신 객체 해제
            {
                BufferManager.Inst.FreeBuffer(mReceiveEventArgs);
                mReceiveEventArgs.SetBuffer(null, 0, 0);
                if (mReceiveEventArgs.BufferList != null)
                    mReceiveEventArgs.BufferList = null;

                mReceiveEventArgs.UserToken = null;
                mReceiveEventArgs.RemoteEndPoint = null;
                mReceiveEventArgs.Completed -= OnReceiveCompleted;

                mReceiveEventArgs.Dispose();
                //풀링하지 않는 경우엔 반드시, m_send_event_args.Dispose();

                mReceiveEventArgs = null;
            }

            //송신 객체 해제
            {
                BufferManager.Inst.FreeBuffer(m_send_event_args);

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

    public class User
    {
        // 유저 정보
        public UserToken m_token;
        public int m_uid;
        public GameRoom m_room;

        public User(UserToken userToken)
        {
            m_token = userToken;
            m_uid = ServerManager.Inst.m_nextUid++;
        }

        public void ProcessPacket(Packet packet)
        {
            if (packet.m_type == (short)PacketType.PACKET_USER_CLOSED)
            {
                m_token.Close();
                return;
            }

            var message = "";

            switch ((PacketType)packet.m_type)
            {
                case PacketType.PACKET_USER_CLOSED:
                    m_token.Close();
                    break;
                case PacketType.ROOM_BROADCAST:
                case PacketType.ROOM_OPPONENT:
                    message = MessagePacket.Deserialize(packet.m_data).m_message;
                    if (m_room != null)
                    {
                        Console.WriteLine(m_uid + ") send message \"" + message + "\"");
                        m_room.SendToOpponent(m_uid, message);
                    }
                    break;
                case PacketType.ROOM_ENTER:
                    if (m_room == null)
                    {
                        Console.WriteLine(m_uid + ") Entering room");
                        ServerManager.Inst.EnterGameRoom(this);
                    }
                    break;
                case PacketType.TEST_PACKET:
                default:
                    message = TestPacket.Deserialize(packet.m_data).m_message;
                    Console.WriteLine(message);
                    m_token.Send(packet);
                    break;
            }
        }
    }
}
