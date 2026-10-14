using System.Collections.Generic;

namespace ImageTextDetector.Models
{
    public class ImageAnalysisResult
    {
        public bool HasText { get; set; }
        public bool HasPeople { get; set; }
        public List<TextRegion> TextRegions { get; set; }

        public ImageAnalysisResult()
        {
            HasText = false;
            HasPeople = false;
            TextRegions = new List<TextRegion>();
        }

        public ImageAnalysisResult(bool hasText, bool hasPeople, List<TextRegion> textRegions)
        {
            HasText = hasText;
            HasPeople = hasPeople;
            TextRegions = textRegions;
        }
    }
}
