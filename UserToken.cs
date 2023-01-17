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

        public UserToken()
        {
            mMessageResolver = new MessageResolver();
        }

        public void Init()
        {
            mReceiveEventArgs = new SocketAsyncEventArgs();
            mReceiveEventArgs.Completed += OnReceiveCompleted;
            mReceiveEventArgs.UserToken = this;

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
            // 서버에서 유저로 패킷 전송
            // Buffer나 pooling 처리 필요??
            SocketAsyncEventArgs sendEventArgs = new SocketAsyncEventArgs();

            // 전송이 완료되면 OnSendCompleted 콜백함수 호출
            sendEventArgs.Completed += OnSendCompleted;
            sendEventArgs.UserToken = this;

            byte[] sendData = packet.GetSendBytes();
            sendEventArgs.SetBuffer(sendData, 0, sendData.Length);

            bool pending = mSocket.SendAsync(sendEventArgs);
            if (!pending)
                OnSendCompleted(null, sendEventArgs);
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
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // 전송 성공
            }
            else
            {

            }

            // 나중에 pooling 처리 필요 (메모리 릭 막기 위함)
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

            // 버퍼 재사용 위해 버퍼 비워줌
            BufferManager.Inst.FreeBuffer(mReceiveEventArgs);

            if (mReceiveEventArgs != null)
                mReceiveEventArgs.Dispose();
            mReceiveEventArgs = null;
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
                    message = TestPacket.Deserialize(packet.m_data).message;
                    Console.WriteLine(message);
                    m_token.Send(packet);
                    break;
            }
        }
    }
}
