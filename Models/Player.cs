using System;

namespace GolfApp1.Models
{
    public class Player
    {
        public string Id { get; set; } = string.Empty;
        public string ClubShortName { get; set; } = string.Empty; // 4-char club short
        public string Code { get; set; } = string.Empty;          // 6-digit unique code
        public string Name { get; set; } = string.Empty;          // up to 20 chars, unique
        public string IndexValue { get; set; } = string.Empty;    // xx.x
        public string Note { get; set; } = string.Empty;          // up to 20 chars
    }
}
