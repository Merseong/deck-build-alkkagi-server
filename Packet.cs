using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class Packet
    {
        public Int16 Type { get; set; }
        public byte[] Data { get; set; }

        public Packet() { }

        public Packet(Packet p)
        {
            Type = p.Type;
            Array.Copy(p.Data, Data, p.Data.Length);
        }

        public Packet Pack<T>(PacketType type, Data<T> data) where T : class
        {
            Type = (short)type;
            var serializedData = data.Serialize();
            SetData(serializedData, serializedData.Length);

            return this;
        }

        public void SetData(byte[] data, int len)
        {
            Data = new byte[len];
            Array.Copy(data, Data, len);
        }

        public byte[] GetSendBytes()
        {
            byte[] type_bytes = BitConverter.GetBytes(Type);
            int header_size = (int)(Data.Length);
            byte[] header_bytes = BitConverter.GetBytes(header_size);
            byte[] send_bytes = new byte[header_bytes.Length + type_bytes.Length + Data.Length];

            //헤더 복사. 헤더 == 데이터의 크기
            Array.Copy(header_bytes, 0, send_bytes, 0, header_bytes.Length);

            //타입 복사
            Array.Copy(type_bytes, 0, send_bytes, header_bytes.Length, type_bytes.Length);

            //데이터 복사
            Array.Copy(Data, 0, send_bytes, header_bytes.Length + type_bytes.Length, Data.Length);

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
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    public class PacketRes : Data<PacketRes>
    {
        public bool isSucess;
        public int testIntValue;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string message = "";

        public PacketRes() { }
    }

    public enum PacketType
    {
        UNDEFINED,
        PACKET_USER_CLOSED,
        PACKET_TEST,
        /// <summary>
        /// 접속한 유저에게 기본 정보를 알려주기 위해 사용
        /// </summary>
        /// <remarks>
        /// MessagePacket(임시)<br/>
        /// senderId 송신 -> 해당 플레이어의 networkID
        /// </remarks>
        PACKET_INFO,
        ROOM_BROADCAST,
        /// <summary>
        /// 룸의 상대편에게 메세지를 보낼 때 사용
        /// </summary>
        /// <remarks>
        /// MessagePacket<br/>
        /// senderId: 보내는 사람<br/>
        /// message: 보낼 내용
        /// </remarks>
        ROOM_OPPONENT,
        /// <summary>
        /// 룸 컨트롤(참여, 퇴장 등)시 사용
        /// </summary>
        /// <remarks>
        /// MessagePacket<br/>
        /// ENTER
        /// ENTERED
        /// LOADED
        /// START
        /// EXIT
        /// </remarks>
        ROOM_CONTROL,
        SYNCVAR_INIT,
        SYNCVAR_CHANGE,
        PACKET_COUNT
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class TestPacket : Data<TestPacket>
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string message;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class MessagePacket : Data<MessagePacket>
    {
        public int senderID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string message;

        public MessagePacket() { }
        public MessagePacket(MessagePacket msg)
        {
            senderID = msg.senderID;
            message = msg.message;
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class SyncVarPacket : Data<SyncVarPacket>
    {
        public uint NetID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
        public byte[] Data;
    }
}
