
using System;

namespace GolfApp1.Models
{
    public sealed class ResultRecord
    {
        public string Id { get; set; } = string.Empty;           // GUID
        public DateTime Date { get; set; }
        public string Club { get; set; } = string.Empty;         // club short name
        public string Venue { get; set; } = string.Empty;

        public string PlayerId { get; set; } = string.Empty;     // FK to Players.Id (optional)
        public string PartnerId { get; set; } = string.Empty;    // FK to Players.Id (optional)

        public string PlayerName { get; set; } = string.Empty;
        public string Partner { get; set; } = string.Empty;

        public int Hcp { get; set; }
        public int Result { get; set; }
        public int Position { get; set; }
    }
}