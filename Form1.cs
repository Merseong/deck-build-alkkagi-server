using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace alkkagi_server
{
    public partial class Form1 : Form
    {
        Listener listener;
        bool isServerActive = false;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (isServerActive)
            {
                Console.WriteLine("이미 서버가 실행중입니다.");
                return;
            }
            Console.WriteLine("ON");
            listener = new Listener();
            listener.StartServer("0.0.0.0", 3333, 100);
            isServerActive = true;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!isServerActive)
            {
                Console.WriteLine("실행중인 서버가 없습니다.");
                return;
            }
            Console.WriteLine("OFF");
            listener.StopServer();
            isServerActive = false;
        }
    }
}
