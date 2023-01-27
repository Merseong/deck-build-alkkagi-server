using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace alkkagi_server
{
    public class MatchQueue
    {
        private List<Ticket> ticketList;
        public bool ThreadLive { get; set; } = true;

        public List<Ticket> TicketList => ticketList;
        private object mutexTicketList = new object();

        public MatchQueue()
        {
            ticketList = new List<Ticket>();

            Thread thread = new Thread(DoMatch);
            thread.Start();
        }

        public void AddTicket(Ticket ticket)
        {
            lock (mutexTicketList)
            {
                ticketList.Add(ticket);
            }
        }

        private void DoMatch()
        {
            while (ThreadLive)
            {
                Thread.Sleep(500);

                // TODO: MMR 및 입장 시간별 매칭 알고리즘 필요
                lock (mutexTicketList)
                {
                    while (ticketList.Count() >= 2)
                    {
                        var ticketPair = ticketList.OrderBy(g => Guid.NewGuid()).Take(2).ToArray();

                        ServerManager.Inst.EnterGameRoom(ticketPair[0].user, ticketPair[1].user);
                        //Console.WriteLine($"User1 id: {ticketPair[0].user.UID}, User2 id: {ticketPair[1].user.UID}");

                        ticketList.Remove(ticketPair[0]);
                        ticketList.Remove(ticketPair[1]);
                    }
                }
            }
        }

        // 매치메이킹시 사용하는 단위 클래스
        public class Ticket
        {
            public User user;
            public uint mmr;
            public DateTime enterTime;
            public bool isExpired = false;

            public Ticket(User user, uint mmr, DateTime enterTime)
            {
                this.user = user;
                this.mmr = mmr;
                this.enterTime = enterTime;
            }
        }
    }
}
