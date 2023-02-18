using System;
using System.Text;

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

    public override string ToString()
    {
        return $"{(PacketType)Type} | {Data.Length}";
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

        //��� ����. ��� == �������� ũ��
        Array.Copy(header_bytes, 0, send_bytes, 0, header_bytes.Length);

        //Ÿ�� ����
        Array.Copy(type_bytes, 0, send_bytes, header_bytes.Length, type_bytes.Length);

        //������ ����
        Array.Copy(Data, 0, send_bytes, header_bytes.Length + type_bytes.Length, Data.Length);

        return send_bytes;
    }
}


[Serializable]
public class Data<T> where T : class
{
    public Data() { }

    public byte[] Serialize()
    {
        var json = UnityEngine.JsonUtility.ToJson(this);
        return Encoding.UTF8.GetBytes(json);
        /**
        var formatter = new BinaryFormatter();
        // Ŭ������ ����ȭ�Ͽ� ������ ������
        byte[] data;
        using (MemoryStream stream = new())
        {
            formatter.Serialize(stream, this);
            data = new byte[stream.Length];
            //��Ʈ���� byte[] �����ͷ� ��ȯ�Ѵ�.
            data = stream.GetBuffer();
        }
        return data;*/
        /**
        var array = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(this, ptr, true);
        Marshal.Copy(ptr, array, 0, size);
        Marshal.FreeHGlobal(ptr);
        return array;*/
    }

    public static T Deserialize(byte[] array)
    {
        var data = Encoding.UTF8.GetString(array);

        return (T)UnityEngine.JsonUtility.FromJson(data, typeof(T));

        /**
        var formatter = new BinaryFormatter();
        using MemoryStream stream = new(array, 0, array.Length);
        // byte�� �о���δ�.
        stream.Write(array, 0, array.Length);
        // Stream seek�� �� ó������ ������.
        stream.Seek(0, SeekOrigin.Begin);
        stream.Position = 0;
        // Ŭ������ ������ȭ
        var data = formatter.Deserialize(stream);*/
        /**
        var size = Marshal.SizeOf(typeof(T));
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(array, 0, ptr, size);
        var s = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);
        return s;*/
    }
}

[Serializable]
public class PacketRes : Data<PacketRes>
{
    public bool isSucess;
    public int testIntValue;

    public string message = "";

    public PacketRes() { }
}

public enum PacketType
{
    UNDEFINED,
    PACKET_USER_CLOSED,
    PACKET_TEST,
    /// <summary>
    /// ������ �������� �⺻ ������ �˷��ֱ� ���� ���
    /// </summary>
    /// <remarks>
    /// MessagePacket(�ӽ�)<br/>
    /// senderId �۽� -> �ش� �÷��̾��� networkID
    /// </remarks>
    PACKET_INFO,
    ROOM_BROADCAST,
    /// <summary>
    /// ���� ������� �޼����� ���� �� ���
    /// </summary>
    /// <remarks>
    /// MessagePacket<br/>
    /// senderId: ������ ���<br/>
    /// message: ���� ����
    /// </remarks>
    ROOM_OPPONENT,
    /// <summary>
    /// ShootStonePacket<br/> ���� �����ӿ� ���� ��Ŷ
    /// </summary>
    ROOM_OPPO_SHOOTSTONE,
    /// <summary>
    /// �� ��Ʈ��(����, ���� ��)�� ���
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
public class TestPacket : Data<TestPacket>
{
    public string message;
}

[Serializable]
public class MessagePacket : Data<MessagePacket>
{
    public int senderID;
    public string message;

    public MessagePacket() { }
    public MessagePacket(MessagePacket msg)
    {
        senderID = msg.senderID;
        message = msg.message;
    }
}

[Serializable]
public class SyncVarPacket : Data<SyncVarPacket>
{
    public uint NetID;
    public byte[] Data;
}

[Serializable]
public class StoneActionPacket : Data<StoneActionPacket>
{
    public int senderID;

    public VelocityRecord[] velocityRecords;
    public PositionRecord[] positionRecords;
    public EventRecord[] eventRecords;

    public short velocityCount;
    public short positionCount;
    public short eventCount;

    public short finalCost; // ���� �ڽ�Ʈ
    public short finalHand; // ���� �ڵ�
}

[Serializable]
public struct VelocityRecord
{
    public float time;
    public int stoneId;
    public float xVelocity;
    public float zVelocity;
}

[Serializable]
public struct PositionRecord
{
    public int stoneId;
    public float xPosition;
    public float zPosition;
}

[Serializable]
// collide, drop out, stone power
public struct EventRecord
{
    public float time;
    public int stoneId;
    public string eventMessage;
    public EventEnum eventEnum; // -> EventEnum
    public float xPosition;
    public float zPosition;
}

[Serializable]
public enum EventEnum
{
    NULL,
    SPENDTOKEN,
    COLLIDE,
    GUARDCOLLIDE,
    DROPOUT,
    POWER,
    COUNT,
}
