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
        User user; // 유저 정보 저장
        public User User { get { return user; } set { user = value; } }
        SocketAsyncEventArgs receiveEventArgs; // 메세지 받을 때 사용
        MessageResolver messageResolver;
        Socket socket;

        public delegate void OnMessageCompletedDelegate(User user, Packet p);
        public OnMessageCompletedDelegate OnMessageCompleted;

        List<Packet> packetList = new List<Packet>(5);
        object mutexPacketList = new object();

        SocketAsyncEventArgs sendEventArgs;

        Queue<Packet> sendPacketQueue = new Queue<Packet>(100);
        object mutexSendList = new object();

        public UserToken(Socket socket)
        {
            this.socket = socket;
            this.socket.NoDelay = true;
            this.socket.ReceiveTimeout = 60 * 1000;
            this.socket.SendTimeout = 60 * 1000;

            messageResolver = new MessageResolver();

            receiveEventArgs = new SocketAsyncEventArgs();
            receiveEventArgs.Completed += OnReceiveCompleted;
            receiveEventArgs.UserToken = this;

            OnMessageCompleted = new OnMessageCompletedDelegate((_, p) =>
            {
                Console.WriteLine($"[{user.UID}] receive packet with type '{(PacketType)p.Type}'");
            });

            sendEventArgs = new SocketAsyncEventArgs();
            sendEventArgs.Completed += OnSendCompleted;
            sendEventArgs.UserToken = this;

            //byte배열을 크게 미리 세팅하고, 그 배열을 재사용한다.
            //args.SetBuffer(m_buffer, m_current_index, m_buffer_size);
            //아래 따로 코드 첨부
            BufferManager.Inst.SetBuffer(sendEventArgs);
            BufferManager.Inst.SetBuffer(receiveEventArgs);
        }

        public void StartReceive()
        {
            // 패킷 송신 대기
            // 패킷이 오면 OnReceiveCompleted 콜백함수 호출
            // MessageResolver를 통해 바이트 배열을 패킷으로 변환
            bool pending = socket.ReceiveAsync(receiveEventArgs);
            if (!pending)
                OnReceiveCompleted(this, receiveEventArgs);
        }

        public void Send(Packet packet)
        {
            if (socket == null)
            {
                return;
            }

            lock (mutexSendList)
            {
                //수신 중인 패킷이 없으면 바로, 전송
                if (sendPacketQueue.Count < 1)
                {
                    sendPacketQueue.Enqueue(packet);
                    SendProcess();
                    return;
                }

                //수신 중인 패킷이 있으면, 큐에 넣고 나감.
                //쌓인 패킷이 100개가 넘으면 그 다음부터는 무시함. 제 겜은 그래도 됨..        
                if (sendPacketQueue.Count < 100)
                    sendPacketQueue.Enqueue(packet);
            }
        }

        private void SendProcess()
        {
            if (socket == null)
            {
                return;
            }

            Packet packet = sendPacketQueue.Peek();
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

                bool pending = socket.SendAsync(send_event_args);
                if (!pending)
                    OnSendCompletedPooling(null, send_event_args);
            }
            else
            {
                //버퍼풀에서 설정한 크기보다 작은 경우(4K 이하)

                //버퍼를 설정
                sendEventArgs.SetBuffer(sendEventArgs.Offset, send_data.Length);
                //버퍼에 데이터 복사
                Array.Copy(send_data, 0, sendEventArgs.Buffer, sendEventArgs.Offset, send_data.Length);

                bool pending = socket.SendAsync(sendEventArgs);
                if (!pending)
                    OnSendCompleted(null, sendEventArgs);
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                // 전송 성공
                messageResolver.OnReceive(e.Buffer, e.Offset, e.BytesTransferred, OnMessageCompletedCallback);

                StartReceive();
            }
            else
            {
                Console.WriteLine(e.SocketError.ToString());
                Packet packet = new Packet();
                packet.Type = (Int16)PacketType.PACKET_USER_CLOSED;

                AddPacket(packet);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                lock (mutexSendList)
                {
                    if (sendPacketQueue.Count > 0)
                        sendPacketQueue.Dequeue();

                    if (sendPacketQueue.Count > 0)
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
                lock (mutexSendList)
                {
                    if (sendPacketQueue.Count > 0)
                        sendPacketQueue.Dequeue();

                    if (sendPacketQueue.Count > 0)
                        SendProcess();
                }
            }
            else
            {
            }
        }

        private void OnMessageCompletedCallback(Packet packet)
        {
            OnMessageCompleted(user, packet);
        }

        private void AddPacket(Packet packet)
        {
            // 처리 부분과 패킷을 넣는 부분의 스레드가 다르기 때문에 락을 걸음
            lock (mutexPacketList)
            {
                packetList.Add(packet);
            }
        }

        //다른 스레드에서 호출된다.
        //때문에 패킷 리스트에 넣고, 처리하기 전에 락을 걸어준다.
        public void Update()
        {

            //완성된 패킷을 매 루프 처리해 준다.
            if (packetList.Count > 0)
            {
                lock (mutexPacketList)
                {
                    try
                    {
                        foreach (Packet packet in packetList)
                            user.ProcessPacket(packet);
                        packetList.Clear();
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
                if (socket != null)
                    socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                if (socket != null)
                    socket.Close();
            }

            socket = null;
            user = null;
            messageResolver.ClearBuffer();

            lock (mutexPacketList)
            {
                packetList.Clear();
            }

            lock (mutexSendList)
            {
                sendPacketQueue.Clear();
            }

            //수신 객체 해제
            {
                BufferManager.Inst.FreeBuffer(receiveEventArgs);
                receiveEventArgs.SetBuffer(null, 0, 0);
                if (receiveEventArgs.BufferList != null)
                    receiveEventArgs.BufferList = null;

                receiveEventArgs.UserToken = null;
                receiveEventArgs.RemoteEndPoint = null;
                receiveEventArgs.Completed -= OnReceiveCompleted;

                receiveEventArgs.Dispose();
                //풀링하지 않는 경우엔 반드시, m_send_event_args.Dispose();

                receiveEventArgs = null;
            }

            //송신 객체 해제
            {
                BufferManager.Inst.FreeBuffer(sendEventArgs);

                if (sendEventArgs.BufferList != null)
                    sendEventArgs.BufferList = null;

                sendEventArgs.UserToken = null;
                sendEventArgs.RemoteEndPoint = null;
                sendEventArgs.Completed -= OnSendCompleted;

                sendEventArgs.Dispose();
                //풀링하지 않는 경우엔 반드시, m_send_event_args.Dispose();

                sendEventArgs = null;
            }
        }
    }

    public class User
    {
        // 유저 정보
        private UserToken token;
        private UserData data;
        public int UID { get; set; }
        public GameRoom Room { get; set; }

        public UserToken UserToken => token;
        public UserData UserData => data;

        public User(UserToken userToken)
        {
            token = userToken;
            UID = ServerManager.Inst.NextId;

            data = new UserData();
        }

        public void ProcessPacket(Packet packet)
        {
            if (packet.Type == (short)PacketType.PACKET_USER_CLOSED)
            {
                token.Close();
                return;
            }

            var message = "";

            switch ((PacketType)packet.Type)
            {
                case PacketType.PACKET_USER_CLOSED:
                    token.Close();
                    break;
                case PacketType.ROOM_BROADCAST:
                case PacketType.ROOM_OPPONENT:
                    message = MessagePacket.Deserialize(packet.Data).message;
                    if (Room != null)
                    {
                        Console.WriteLine(UID + ") send message \"" + message + "\"");
                        Room.SendToOpponent(UID, message);
                    }
                    break;
                case PacketType.ROOM_ENTER:
                    if (Room == null)
                    {
                        Console.WriteLine(UID + ") is matchmaking");
                        ServerManager.Inst.MatchQueue.AddTicket(
                            new MatchQueue.Ticket(this, 1000, DateTime.Now));
                    }
                    break;
                case PacketType.SYNCVAR_INIT:
                    if (Room == null) return;
                    Room.InitSyncVar(this, SyncVarPacket.Deserialize(packet.Data));
                    break;
                case PacketType.SYNCVAR_CHANGE:
                    if (Room == null) return;
                    Room.ChangeSyncVar(this, SyncVarPacket.Deserialize(packet.Data));
                    break;
                case PacketType.PACKET_TEST:
                default:
                    message = TestPacket.Deserialize(packet.Data).message;
                    Console.WriteLine(message);
                    token.Send(packet);
                    break;
            }
        }
    }

    // DB와 연계해서 쓸 구조체
    public struct UserData
    {
        public uint mmr;

        public UserData(uint mmr)
        {
            this.mmr = mmr;
        }
    }
}
