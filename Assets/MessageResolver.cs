using System;

public class MessageResolver
{
    public delegate void CompletedMessageCallback(Packet packet);

    int mMessageSize;
    byte[] mMessageBuffer = new byte[1024 * 2000];
    byte[] mHeaderBuffer = new byte[4];
    byte[] mTypeBuffer = new byte[2];

    PacketType mPreType;

    int mHeadPosition;
    int mTypePosition;
    int mCurrentPosition;

    short mMessageType;
    int mRemainBytes;

    bool mHeadCompleted;
    bool mTypeCompleted;
    bool mCompleted;

    CompletedMessageCallback mCompletedCallback;

    public MessageResolver()
    {
        ClearBuffer();
    }

    public void OnReceive(byte[] buffer, int offset, int transffered, CompletedMessageCallback callback)
    {
        // ���� ���� �������� ��ġ
        int srcPosition = offset;

        // �ݹ��Լ� ����
        mCompletedCallback = callback;

        // ���� ������ ��
        mRemainBytes = transffered;

        if (!mHeadCompleted)
        {
            mHeadCompleted = ReadHead(buffer, ref srcPosition);

            if (!mHeadCompleted) return;

            mMessageSize = GetBodySize();

            // ������ ���Ἲ �˻�
            if (mMessageSize < 0)
                return;
        }

        if (!mTypeCompleted)
        {
            mTypeCompleted = ReadType(buffer, ref srcPosition);

            if (!mTypeCompleted)
                return;

            mMessageType = BitConverter.ToInt16(mTypeBuffer, 0);

            if (mMessageType < 0 ||
                mMessageType > (int)PacketType.PACKET_COUNT - 1)
                return;

            mPreType = (PacketType)mMessageType;
        }

        if (!mCompleted)
        {
            mCompleted = ReadBody(buffer, ref srcPosition);
            if (!mCompleted)
                return;
        }

        // �����Ͱ� �ϼ��Ǹ� ��Ŷ���� ��ȯ
        Packet packet = new Packet
        {
            Type = mMessageType
        };
        packet.SetData(mMessageBuffer, mMessageSize);

        mCompletedCallback(packet);

        ClearBuffer();
    }

    public void ClearBuffer()
    {
        Array.Clear(mMessageBuffer, 0, mMessageBuffer.Length);
        Array.Clear(mHeaderBuffer, 0, mHeaderBuffer.Length);
        Array.Clear(mTypeBuffer, 0, mTypeBuffer.Length);

        mMessageSize = 0;
        mHeadPosition = 0;
        mTypePosition = 0;
        mCurrentPosition = 0;
        mMessageType = 0;
        mRemainBytes = 0;

        mHeadCompleted = false;
        mTypeCompleted = false;
        mCompleted = false;
    }

    private bool ReadHead(byte[] buffer, ref int srcPosition)
    {
        return ReadUntil(buffer, ref srcPosition, mHeaderBuffer, ref mHeadPosition, 4);
    }

    private bool ReadType(byte[] buffer, ref int srcPosition)
    {
        return ReadUntil(buffer, ref srcPosition, mTypeBuffer, ref mTypePosition, 2);
    }

    private bool ReadBody(byte[] buffer, ref int srcPosition)
    {
        return ReadUntil(buffer, ref srcPosition, mMessageBuffer, ref mCurrentPosition, mMessageSize);
    }

    private bool ReadUntil(byte[] buffer, ref int srcPosition, byte[] destBuffer, ref int destPosition, int toSize)
    {
        if (mRemainBytes < 0)
            return false;

        int copySize = toSize - destPosition;
        if (mRemainBytes < copySize)
            copySize = mRemainBytes;

        Array.Copy(buffer, srcPosition, destBuffer, destPosition, copySize);

        // ���� ��ġ�� �Ű��ش�
        srcPosition += copySize;
        destPosition += copySize;
        mRemainBytes -= copySize;

        return !(destPosition < toSize);
    }

    // ����κ��� ������ ��ü ũ�⸦ �о�´�
    private int GetBodySize()
    {
        return BitConverter.ToInt16(mHeaderBuffer, 0);
    }
}
