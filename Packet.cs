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
            byte[] send_bytes = new byte[header_bytes.Length + type_bytes.Length + m_data.Length];

            //헤더 복사. 헤더 == 데이터의 크기
            Array.Copy(header_bytes, 0, send_bytes, 0, header_bytes.Length);

            //타입 복사
            Array.Copy(type_bytes, 0, send_bytes, header_bytes.Length, type_bytes.Length);

            //데이터 복사
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
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    public class PacketRes : Data<PacketRes>
    {
        public bool m_is_sucess;
        public int m_test_int_value;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string m_message = "";

        public PacketRes() { }
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

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    public class TestPacket : Data<TestPacket>
    {
        public bool isSucess;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1000)]
        public string message;

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
}
