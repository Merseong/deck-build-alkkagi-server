using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace alkkagi_server
{
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
