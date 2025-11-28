using System;

namespace GolfApp1.Models
{
    public sealed class ParsedPlayerRecord
    {
        public int Page { get; set; }

        public string RawLine { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string HandicapIndex { get; set; } = string.Empty;

        public string Result { get; set; } = string.Empty;

        // Confidence 0.0 - 1.0
        public double Confidence { get; set; }
    }
}