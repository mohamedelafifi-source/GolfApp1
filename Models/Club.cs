using System;

namespace GolfApp1.Models
{
    public class Club
    {
        // Primary key (UUID)
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // Exactly 4 characters (UI enforces this)
        public string ShortName { get; set; } = string.Empty;

        // Up to 20 characters
        public string LongName { get; set; } = string.Empty;

        // Number of players (no CHECK enforced here)
        public int NumberOfPlayers { get; set; } = 0;
    }
}
