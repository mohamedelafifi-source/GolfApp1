using System;

namespace GolfApp1.Models
{
    public class Club
    {
        // UUID primary key
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // Exactly 4 characters. Application enforces length.
        public string ShortName { get; set; } = string.Empty;

        // Up to 20 characters. Application enforces length.
        public string LongName { get; set; } = string.Empty;

        // 1..50 enforced by code + database CHECK constraint
        public int NumberOfPlayers { get; set; } = 1;
    }
}