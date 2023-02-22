using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MatchQueue
{
    private List<Ticket> ticketList;
    public bool ThreadLive { get; set; } = true;

    public List<Ticket> TicketList => ticketList;
    private object mutexTicketList = new object();

    public MatchQueue()
    {
        ticketList = new List<Ticket>();
    }

    public void AddTicket(Ticket ticket)
    {
        lock (mutexTicketList)
        {
            if (ticketList.Contains(ticket)) return;
            ticketList.Add(ticket);
        }
    }

    public void RemoveTicket(UserToken user)
    {
        lock (mutexTicketList)
        {
            ticketList.RemoveAll(ticket => ticket.user.UID == user.UID);
        }
    }

    public void DoMatch()
    {
        // TODO: MMR 및 입장 시간별 매칭 알고리즘 필요
        lock (mutexTicketList)
        {
            while (ticketList.Count >= 2)
            {
                var ticketPair = ticketList.OrderBy(g => System.Guid.NewGuid()).Take(2).ToArray();

                MainServer.Inst.EnterGameRoom(ticketPair[0], ticketPair[1]);
                //Console.WriteLine($"User1 id: {ticketPair[0].user.UID}, User2 id: {ticketPair[1].user.UID}");

                ticketList.Remove(ticketPair[0]);
                ticketList.Remove(ticketPair[1]);
            }
        }
    }

    // 매치메이킹시 사용하는 단위 클래스
    public class Ticket
    {
        public UserToken user;
        public uint mmr;
        public float enterTime;
        public bool isExpired = false;
        public string deckCode;
        public int deckCount;

        public Ticket(UserToken user, string deckCode, uint mmr, float enterTime, int deckCount)
        {
            this.user = user;
            this.mmr = mmr;
            this.enterTime = enterTime;
            this.deckCode = deckCode;
            this.deckCount = deckCount;
        }
    }
}
