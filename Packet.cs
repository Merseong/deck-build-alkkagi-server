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
        ROOM_BROADCAST,
        ROOM_OPPONENT,
        ROOM_ENTER,
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

        public TestPacket() { }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class MessagePacket : Data<MessagePacket>
    {
        public int senderID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string message;

        public MessagePacket() { }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class SyncVarPacket : Data<SyncVarPacket>
    {
        public uint NetID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
        public byte[] Data;

        public SyncVarPacket() { }
    }
}
